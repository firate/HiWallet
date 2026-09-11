using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Withdrawals;

/// <summary>
/// Çekimi geri alır: orijinal işlemin TERS KAYDINI yazar (overview.md madde 6).
///
/// <b>Bacaklar orijinalden OKUNUYOR, yeniden hesaplanmıyor.</b> Tasarımın can alıcı
/// noktası bu. Ters kayıt politikadan yeniden üretilseydi — komisyonu tekrar hesapla,
/// clearing'i tekrar bul — üç bacaktan biri unutulabilirdi. Özellikle
/// <c>revenue</c>: o bacak atlanınca kayıt YİNE DENGELİ olur (cüzdan +100,
/// clearing -100), zero-sum trigger'ı susar ve hiçbir test kendiliğinden patlamaz —
/// ama müşteri gerçekleşmemiş bir işlemin komisyonunu ödemiş kalır
/// (CLAUDE.md "Withdrawal saga").
///
/// Orijinali okuyup her bacağı negatifleyerek bu hata sınıfı yapısal olarak imkânsız
/// hale geliyor: kaç bacak yazıldıysa o kadarı geri alınıyor, tutarlar birebir.
/// <see cref="RefundWithdrawal"/>'ın tutar taşımaması da bu yüzden.
///
/// Orijinal kayıt SİLİNMİYOR; ledger append-only (CLAUDE.md "Ledger").
/// </summary>
public sealed class RefundWithdrawalHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock,
    ILogger<RefundWithdrawalHandler> logger)
{
    private const int MaxAttempts = 3;

    public static string IdempotencyKey(Guid sagaId) => $"withdrawal-refund:{sagaId}";

    public async Task<WithdrawalReply> HandleAsync(RefundWithdrawal command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RefundAsync(command, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogDebug(
                    "İade çakıştı, yeniden deneniyor. Deneme {Attempt}/{Max}, saga {SagaId}",
                    attempt, MaxAttempts, command.SagaId);
            }
        }
    }

    private async Task<WithdrawalReply> RefundAsync(RefundWithdrawal command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var original = await LoadOriginalAsync(db, command.SagaId, ct);

        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        // Her bacak birebir tersine çevriliyor. Hesap kimliğine göre ARTAN sıra —
        // düşme ile iade aynı hesap üçlüsüne dokunuyor ve ters sıralarsak deadlock
        // olur (decisions.md madde 8).
        var legs = original.Entries
            .Select(entry => (entry.LedgerAccountId, Delta: entry.Money.Negated))
            .OrderBy(leg => leg.LedgerAccountId)
            .ToArray();

        foreach (var (ledgerAccountId, delta) in legs)
        {
            var account = await db.LedgerAccounts.FirstOrDefaultAsync(a => a.Id == ledgerAccountId, ct)
                          ?? throw new InvalidOperationException($"Ledger hesabı yok: {ledgerAccountId}");

            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(b => b.LedgerAccountId == ledgerAccountId, ct)
                          ?? throw new InvalidOperationException($"Bakiye satırı yok: {ledgerAccountId}");

            balance.Apply(delta, account.CanGoNegative, now);
        }

        var reply = WithdrawalReply.For(new WithdrawalRefunded
        {
            SagaId = command.SagaId,
            LedgerTransactionId = transactionId
        });

        var claimed = await ClaimAsync(db, command, reply, transactionId, now, ct);

        if (!claimed)
        {
            await transaction.RollbackAsync(ct);

            return await ReadStoredReplyAsync(command, ct);
        }

        // Aktör KOMUTTAN (decisions.md madde 34). Bankanın reddinde `system` geliyor
        // — kimse istemedi, saga karar verdi. Backoffice'ten iptal edildiğinde o
        // çalışan geliyor ve kalıcı kayıtta kim karar verdi görünüyor.
        //
        // Sabit `system` yazmak bu ayrımı imkânsız kılardı: parayı geri vermeye karar
        // veren insanın izi hiç oluşmazdı.
        var tx = LedgerTransaction.Create(
            transactionId,
            LedgerTransactionType.Refund,
            original.LedgerAccountId,
            Actor.From(command.Actor),
            now,
            IdempotencyKey(command.SagaId),
            correlationId: command.SagaId);

        foreach (var (ledgerAccountId, delta) in legs)
        {
            tx.AddEntry(ledgerAccountId, delta);
        }

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Çekim iade edildi. Saga {SagaId}, {LegCount} bacak, orijinal {OriginalId} → {TransactionId}",
            command.SagaId, legs.Length, original.Id, transactionId);

        return reply;
    }

    /// <summary>
    /// Geri alınacak düşme işlemi. <c>CorrelationId</c> üzerinden bulunuyor — düşme
    /// handler'ı saga kimliğini oraya yazıyor.
    ///
    /// Bulunamaması bir iş kuralı reddi DEĞİL, tutarsızlık: orchestrator iade komutunu
    /// yalnızca düşme tamamlandıktan sonra üretiyor. <see cref="DomainException"/>
    /// fırlatılmıyor ki saga'ya "iade edildi" denmesin — mesaj dead-letter'a gidip
    /// inceleniyor.
    /// </summary>
    private static async Task<LedgerTransaction> LoadOriginalAsync(
        WalletDbContext db, Guid sagaId, CancellationToken ct)
    {
        var original = await db.LedgerTransactions
                           .Include(t => t.Entries)
                           .FirstOrDefaultAsync(
                               t => t.CorrelationId == sagaId
                                    && t.Type == LedgerTransactionType.Withdrawal,
                               ct)
                       ?? throw new InvalidOperationException(
                           $"Saga {sagaId} için düşme işlemi bulunamadı; iade edilecek bir şey yok.");

        if (original.Entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"Düşme işlemi {original.Id} bacaksız görünüyor; iade yazılamaz.");
        }

        return original;
    }

    private static async Task<bool> ClaimAsync(
        WalletDbContext db,
        RefundWithdrawal command,
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
                 ({command.CommandId}, {nameof(RefundWithdrawal)}, {command.SagaId}, {now},
                  {ledgerTransactionId}, {reply.RoutingKey}, {reply.Payload}::jsonb)
             ON CONFLICT (message_id) DO NOTHING
             """,
            ct);

        return inserted == 1;
    }

    private async Task<WithdrawalReply> ReadStoredReplyAsync(
        RefundWithdrawal command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var stored = await db.ProcessedMessages
                         .AsNoTracking()
                         .FirstOrDefaultAsync(m => m.MessageId == command.CommandId, ct)
                     ?? throw new InvalidOperationException(
                         $"Komut {command.CommandId} sahiplenilemedi ama kaydı da yok. Yeniden denenmeli.");

        logger.LogInformation(
            "İade komutu zaten işlenmiş, saklanan cevap dönülüyor. Saga {SagaId}", command.SagaId);

        return new WithdrawalReply(stored.ReplyRoutingKey, stored.ReplyPayload, Replayed: true);
    }
}
