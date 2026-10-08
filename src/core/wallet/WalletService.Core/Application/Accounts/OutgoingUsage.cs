using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Seviye limitinin hesaptan çıkan tarafının okuması: transfer, ödeme ve çekim.
/// <see cref="IncomingUsage"/>'ın eşi. Transfer (wallet-api), çekim (wallet-consumer) ve
/// müşteriye gösterilen limitler (wallet-api) aynı sayımı kullanıyor; iki kopya olsaydı
/// müşteri gördüğünden farklı bir kullanımla reddedilebilirdi.
///
/// Kapsam hesap, cüzdan değil: aksi halde ikinci cüzdan açılarak aşılırdı
/// (decisions.md madde 20). Ay UTC'ye göre, günlük limitteki gün gibi.
/// </summary>
internal static class OutgoingUsage
{
    public static DateTimeOffset StartOfMonth(DateTimeOffset now) =>
        new(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset StartOfDay(DateTimeOffset now) => new(now.UtcDateTime.Date, TimeSpan.Zero);

    /// <summary>
    /// Bu ay hesaptan giden transfer (<see cref="KycMovement.OutgoingTransfer"/>) ya da ödeme
    /// (<see cref="KycMovement.Payment"/>): cüzdandan düşen, komisyon dahil. Hesabın kendi
    /// cüzdanına aktarım başka birine gönderim değil: alacak bacağı da hesabın bir cüzdanındaysa
    /// sayılmıyor.
    /// </summary>
    public static async Task<Money> TransfersThisMonthAsync(
        WalletDbContext db, Guid accountId, KycMovement movement, Currency currency, DateTimeOffset now,
        CancellationToken ct)
    {
        LedgerTransactionType[] types = movement switch
        {
            KycMovement.Payment => [LedgerTransactionType.Payment],
            KycMovement.OutgoingTransfer => [LedgerTransactionType.P2P, LedgerTransactionType.P2B],
            _ => throw new ArgumentException($"{movement} giden bir transfer değil.", nameof(movement))
        };

        var since = StartOfMonth(now);

        var walletIds = db.LedgerAccounts
            .Where(a => a.AccountId == accountId)
            .Select(a => a.Id);

        var debited = await db.LedgerEntries
            .Where(e => walletIds.Contains(e.LedgerAccountId)
                        && e.Amount < 0m
                        && e.Currency == currency
                        && e.CreatedAt >= since
                        && db.LedgerTransactions.Any(t => t.Id == e.TransactionId && types.Contains(t.Type))
                        && !db.LedgerEntries.Any(other => other.TransactionId == e.TransactionId
                                                          && other.Amount > 0m
                                                          && walletIds.Contains(other.LedgerAccountId)))
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new Money(-debited, currency);
    }

    /// <summary>
    /// Verilen andan bu yana hesabın bütün cüzdanlarından çekimle çıkan NET tutar.
    ///
    /// <b>İade edilenler düşülüyor.</b> Transfer sayımı yalnızca kendi tipinin borç
    /// bacaklarını topluyor; çekimde bu yanlış olurdu. Banka reddedip para müşteriye geri
    /// döndüyse o para hesaptan ÇIKMADI ve limiti tüketmemeli (decisions.md madde 22).
    /// İşaretli toplam alınıyor: düşme bacağı negatif, iade bacağı pozitif, ikisi birbirini
    /// götürüyor.
    /// </summary>
    public static async Task<Money> WithdrawnSinceAsync(
        WalletDbContext db, Guid accountId, Currency currency, DateTimeOffset since, CancellationToken ct)
    {
        var walletIds = db.LedgerAccounts
            .Where(a => a.AccountId == accountId)
            .Select(a => a.Id);

        var net = await db.LedgerEntries
            .Where(e => walletIds.Contains(e.LedgerAccountId)
                        && e.Currency == currency
                        && e.CreatedAt >= since
                        && db.LedgerTransactions.Any(t =>
                            t.Id == e.TransactionId
                            && (t.Type == LedgerTransactionType.Withdrawal
                                || t.Type == LedgerTransactionType.Refund)))
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new Money(-net, currency);
    }
}
