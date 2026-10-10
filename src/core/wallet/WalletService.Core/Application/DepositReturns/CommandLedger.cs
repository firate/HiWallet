using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.DepositReturns;

/// <summary>
/// İadenin komutlarının <c>processed_messages</c> kapısı: komut bir kez işleniyor, verilen
/// cevap saklanıyor ve tekrar teslimde ledger'a dokunulmadan yeniden yayınlanıyor
/// (decisions.md madde 32). Çekim handler'larındaki kalıbın aynısı.
/// </summary>
internal static class CommandLedger
{
    /// <summary>Kapı: <c>INSERT ... ON CONFLICT DO NOTHING</c>. Ledger'la aynı transaction'da.</summary>
    /// <returns><c>false</c>: komut daha önce işlenmiş.</returns>
    public static async Task<bool> ClaimAsync(
        WalletDbContext db,
        Guid commandId,
        string commandType,
        Guid sagaId,
        Guid? ledgerTransactionId,
        WithdrawalReply reply,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO processed_messages
                 (message_id, message_type, saga_id, processed_at,
                  ledger_transaction_id, reply_routing_key, reply_payload)
             VALUES
                 ({commandId}, {commandType}, {sagaId}, {now},
                  {ledgerTransactionId}, {reply.RoutingKey}, {reply.Payload}::jsonb)
             ON CONFLICT (message_id) DO NOTHING
             """,
            ct);

        return inserted == 1;
    }

    /// <summary>
    /// Saklanan cevap aynen. Yeniden hesaplanmıyor: aradan geçen sürede durum değişmiş
    /// olabilir ve aynı komuta iki farklı cevap saga'yı bozardı.
    /// </summary>
    public static async Task<WithdrawalReply> StoredReplyAsync(
        IDbContextFactory<WalletDbContext> contextFactory, Guid commandId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var stored = await db.ProcessedMessages
                         .AsNoTracking()
                         .FirstOrDefaultAsync(m => m.MessageId == commandId, ct)
                     // Kapıya takıldık ama satır görünmüyor: öbür işlem henüz commit etmemiş
                     // ya da geri almış. Geçici; mesaj kuyruğa geri konmalı.
                     ?? throw new InvalidOperationException(
                         $"Komut {commandId} sahiplenilemedi ama kaydı da yok. Yeniden denenmeli.");

        return new WithdrawalReply(stored.ReplyRoutingKey, stored.ReplyPayload, Replayed: true);
    }
}
