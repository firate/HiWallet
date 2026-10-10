using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Deposits;

/// <summary>
/// Askıdaki havalenin çözüm adımlarının okunması ve yazılması: aktarım, iadenin başlaması,
/// tamamlanması, geri konması. Aktarım (wallet-api) ve iade (wallet-consumer) aynı kapıdan
/// geçiyor; iki kopya olsaydı biri öbürünün sürdüğü işi göremezdi.
/// </summary>
internal static class SuspendedDepositSteps
{
    /// <summary>Havalenin son adımı; hiç adım yoksa <c>null</c>.</summary>
    public static Task<SuspendedDepositResolution?> LastAsync(WalletDbContext db, Guid depositId, CancellationToken ct) =>
        db.SuspendedDepositResolutions
            .AsNoTracking()
            .Where(r => r.SuspendedDepositId == depositId)
            .OrderByDescending(r => r.Seq)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Adımı son adımın ardına yazar. Kapı anahtar: aynı sırayı alan ikinci yazar
    /// <c>ON CONFLICT DO NOTHING</c>'e takılıyor. Sıranın kendisi okunarak bulunuyor; tekilliğe
    /// yine veritabanı karar veriyor.
    /// </summary>
    /// <returns>
    /// <c>false</c>: araya başka bir karar girdi. Çağıran taze okumayla yeniden denemeli.
    /// </returns>
    public static async Task<bool> AppendAsync(
        WalletDbContext db,
        Guid depositId,
        SuspendedDepositResolution? last,
        DepositResolutionKind kind,
        Guid ledgerTransactionId,
        Guid? accountId,
        string resolvedBy,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var seq = (last?.Seq ?? 0) + 1;

        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO suspended_deposit_resolutions
                 (suspended_deposit_id, seq, kind, ledger_transaction_id, account_id, resolved_by, created_at)
             VALUES ({depositId}, {seq}, {kind.ToText()}, {ledgerTransactionId}, {accountId}, {resolvedBy}, {now})
             ON CONFLICT (suspended_deposit_id, seq) DO NOTHING
             """,
            ct);

        return inserted == 1;
    }

    /// <summary>
    /// Araya başka bir karar girdi: transaction geri alınıp taze okumayla yeniden
    /// deneniyor. Çakışma tipi bu yüzden concurrency; denemeler tükenirse <c>409</c>.
    /// </summary>
    public static DbUpdateConcurrencyException Raced(Guid depositId) =>
        new($"Askıdaki havale {depositId} için aynı anda başka bir karar yazıldı.");
}
