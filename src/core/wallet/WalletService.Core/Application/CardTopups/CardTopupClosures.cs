using HiWallet.WalletService.Domain.CardTopups;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.CardTopups;

/// <summary>
/// Payın kapanış satırı. Pay başına tek satır ve <c>ON CONFLICT DO NOTHING</c> ile yazılıyor:
/// aynı kapanış bildirimle de, kart yüklemesinin başlangıcındaki hatayla da gelebilir;
/// hangisi önce yazarsa onunki geçerli. "Önce SELECT sonra INSERT" YOK (CLAUDE.md).
/// </summary>
internal static class CardTopupClosures
{
    /// <returns>Satır yazıldıysa <c>true</c>; pay zaten kapanmışsa <c>false</c>.</returns>
    public static async Task<bool> CloseAsFailedAsync(
        WalletDbContext db, Guid holdId, DateTimeOffset now, CancellationToken ct)
    {
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO card_topup_hold_closures (hold_id, outcome, ledger_transaction_id, closed_at)
             VALUES ({holdId}, {CardTopupOutcome.Failed.ToText()}, NULL, {now})
             ON CONFLICT (hold_id) DO NOTHING
             """,
            ct);

        return inserted == 1;
    }

    public static Task<CardTopupHoldClosure> ReadAsync(WalletDbContext db, Guid holdId, CancellationToken ct) =>
        db.CardTopupHoldClosures.AsNoTracking().SingleAsync(c => c.HoldId == holdId, ct);
}
