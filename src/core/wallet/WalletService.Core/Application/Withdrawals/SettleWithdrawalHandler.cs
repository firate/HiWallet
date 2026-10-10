using System.Text.Json;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Withdrawals;

/// <summary>
/// Çekimin muhasebesini kapatır (<c>ledger-schema.md</c> "Withdrawal settlement",
/// adım 5.5b).
///
/// <code>
/// clearing         -100     debit'te açılan borç kapanır
/// provider_expense  -1.5    yalnızca Net modelde
/// nostro          +101.5    banka hesabından çıkan gerçek tutar
/// </code>
///
/// <b>Tutar komuttan GELMİYOR, orijinal işlemden okunuyor.</b> Ters kayıttaki
/// (<see cref="RefundWithdrawalHandler"/>) kuralın aynısı: clearing'e ne yazıldığını
/// wallet biliyor ve orchestrator'dan geri göndermek ikinci bir doğruluk kaynağı
/// açardı. Komut yalnızca bankanın ücretini taşıyor — onu yalnızca banka biliyor.
///
/// <b>Invoiced modelde gider bacağı yok</b> (madde 10): ücret dönem sonu faturasında
/// ödeniyor ve burada da yazmak gideri iki kez kaydederdi. O modelde nostro'dan
/// yalnızca müşteriye giden tutar çıkıyor; ücret sonra, faturayla.
/// </summary>
public sealed class SettleWithdrawalHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    ProviderPolicy providers,
    IClock clock,
    ILogger<SettleWithdrawalHandler> logger)
{
    /// <summary>
    /// Çekimin bankası. Bugün tek banka var ve <c>bank-fake</c> hem çekimin clearing
    /// hesabını hem ücret tarifesini taşıyor. İkinci bir banka eklendiğinde bu
    /// bilginin saga'dan gelmesi gerekecek.
    /// </summary>
    private const string BankProvider = "bank-fake";

    public async Task<WithdrawalReply> HandleAsync(SettleWithdrawal command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = clock.UtcNow;

        // --- Orijinal işlem ----------------------------------------------------------
        var original = await db.LedgerTransactions
            .Include(t => t.Entries)
            .FirstOrDefaultAsync(
                t => t.CorrelationId == command.SagaId && t.Type == LedgerTransactionType.Withdrawal, ct)
            ?? throw new InvalidOperationException(
                $"Saga {command.SagaId} için çekim işlemi bulunamadı; settlement yazılamaz.");

        // --- Ledger -------------------------------------------------------------------
        var transactionId = Guid.NewGuid();

        var settled = await BankTransferSettlement.RecordAsync(
            db, providers, BankProvider, original, command.FeeAmount, command.BankReference, transactionId,
            command.SagaId, $"{BankProvider}:withdrawal-settlement:{command.SagaId}", SystemActors.WithdrawalSaga,
            now, ct);

        var reply = WithdrawalReply.For(new WithdrawalSettled
        {
            SagaId = command.SagaId,
            LedgerTransactionId = transactionId
        });

        // Deduplikasyon kapısı: tekrar teslimde ledger'a DOKUNULMUYOR, saklanan cevap
        // yeniden yayınlanıyor (decisions.md madde 32). Kapı ledger'la aynı
        // transaction'da — RefundWithdrawalHandler'daki kalıbın aynısı.
        var claimed = await ClaimAsync(db, command, reply, transactionId, now, ct);

        if (!claimed)
        {
            await transaction.RollbackAsync(ct);

            return await ReadStoredReplyAsync(command, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Çekim muhasebesi kapandı. Saga {SagaId} → {TransactionId}: clearing -{Owed}, " +
            "nostro +{Leaving}, ücret {Fee} ({Model})",
            command.SagaId, transactionId, settled.Owed, settled.LeavingBank, command.FeeAmount, settled.Model);

        return reply;
    }

    private static async Task<bool> ClaimAsync(
        WalletDbContext db,
        SettleWithdrawal command,
        WithdrawalReply reply,
        Guid ledgerTransactionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO processed_messages
                 (message_id, message_type, saga_id, processed_at,
                  ledger_transaction_id, reply_routing_key, reply_payload)
             VALUES
                 ({command.CommandId}, {nameof(SettleWithdrawal)}, {command.SagaId}, {now},
                  {ledgerTransactionId}, {reply.RoutingKey}, {reply.Payload}::jsonb)
             ON CONFLICT (message_id) DO NOTHING
             """,
            ct);

        return inserted == 1;
    }

    private async Task<WithdrawalReply> ReadStoredReplyAsync(
        SettleWithdrawal command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var stored = await db.ProcessedMessages
                         .AsNoTracking()
                         .FirstOrDefaultAsync(m => m.MessageId == command.CommandId, ct)
                     ?? throw new InvalidOperationException(
                         $"Komut {command.CommandId} sahiplenilemedi ama kaydı da yok. Yeniden denenmeli.");

        logger.LogInformation(
            "Settle komutu zaten işlenmiş, saklanan cevap dönülüyor. Saga {SagaId}", command.SagaId);

        return new WithdrawalReply(stored.ReplyRoutingKey, stored.ReplyPayload, Replayed: true);
    }
}
