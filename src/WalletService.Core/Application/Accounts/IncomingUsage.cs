using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Seviye limitinin hesaba gelen tarafının okuması: ayın girişleri ve hesabın bakiyesi.
/// Gelen transfer (wallet-api) ve havale (wallet-consumer) aynı sayımı kullanıyor; iki
/// kopya olsaydı iki yol aynı hesap için farklı toplam görürdü.
///
/// Kapsam hesap, cüzdan değil: aksi halde ikinci cüzdan açılarak aşılırdı
/// (decisions.md madde 20). Ay UTC'ye göre, günlük limitteki gün gibi.
/// </summary>
internal static class IncomingUsage
{
    /// <summary>
    /// Bu ay hesabın cüzdanlarına yüklemeyle ve başka hesaplardan transferle gelen. Yükleme
    /// her <see cref="LedgerTransactionType.Topup"/>: havale de kart da. Transferde işlemin
    /// kapsamı gönderen cüzdan; kendi cüzdanından gelen aktarım o yüzden dışarıda.
    /// </summary>
    public static async Task<IncomingThisMonth> ThisMonthAsync(
        WalletDbContext db, Guid accountId, Currency currency, DateTimeOffset now, CancellationToken ct)
    {
        var since = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var walletIds = db.LedgerAccounts
            .Where(a => a.AccountId == accountId)
            .Select(a => a.Id);

        var credits = db.LedgerEntries
            .Where(e => walletIds.Contains(e.LedgerAccountId)
                        && e.Amount > 0m
                        && e.Currency == currency
                        && e.CreatedAt >= since);

        var deposits = await credits
            .Where(e => db.LedgerTransactions.Any(t => t.Id == e.TransactionId
                                                      && t.Type == LedgerTransactionType.Topup))
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var transfers = await credits
            .Where(e => db.LedgerTransactions.Any(t => t.Id == e.TransactionId
                                                      && (t.Type == LedgerTransactionType.P2P
                                                          || t.Type == LedgerTransactionType.B2P)
                                                      && !walletIds.Contains(t.LedgerAccountId)))
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new IncomingThisMonth(new Money(deposits, currency), new Money(transfers, currency));
    }

    /// <summary>Hesabın bu para birimindeki bütün cüzdanlarının, bütün kovalarının toplamı.</summary>
    public static async Task<Money> BalanceAsync(
        WalletDbContext db, Guid accountId, Currency currency, CancellationToken ct)
    {
        var walletIds = db.LedgerAccounts
            .Where(a => a.AccountId == accountId)
            .Select(a => a.Id);

        var balance = await db.LedgerBalances
            .Where(b => walletIds.Contains(b.LedgerAccountId) && b.Currency == currency)
            .SumAsync(b => (decimal?)b.Balance, ct) ?? 0m;

        return new Money(balance, currency);
    }
}
