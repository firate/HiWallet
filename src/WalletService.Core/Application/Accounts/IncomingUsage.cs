using HiWallet.WalletService.Domain.CardTopups;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Seviye limitinin hesaba gelen tarafının okuması: ayın girişleri ve hesabın bakiyesi.
/// Gelen transfer (wallet-api), havale (wallet-consumer) ve kartla yüklemenin başlangıcı
/// (wallet-api) aynı sayımı kullanıyor; iki kopya olsaydı yollar aynı hesap için farklı
/// toplam görürdü.
///
/// <b>Açık kart yüklemeleri girmiş sayılıyor.</b> Kart limitte öncelikli: başlamış bir
/// ödemenin payı (<see cref="CardTopupHold"/>) hem ayın yüklemelerine hem bakiyeye
/// ekleniyor. Ödeme sürerken gelen para o payla birlikte limiti aşıyorsa reddediliyor;
/// ödeme kapanınca pay düşüyor.
///
/// Kapsam hesap, cüzdan değil: aksi halde ikinci cüzdan açılarak aşılırdı
/// (decisions.md madde 20). Ay UTC'ye göre, günlük limitteki gün gibi.
/// </summary>
internal static class IncomingUsage
{
    /// <summary>
    /// Hesaba gelen paranın limit kararlarını sıraya sokar: hesabın satırı transaction
    /// bitene kadar kilitli. Limit kararı okuyup karar veriyor ve yazdığı şey çoğu zaman
    /// bakiye satırı değil (kart payı, başka bir cüzdan); sıra olmasa aynı hesaba aynı
    /// anda gelen iki para ikisi de limiti boş görürdü. Kilit yalnızca kararın süresince
    /// ve yalnızca seviyesi olan hesapta; işyerine gelen ödemeler beklemiyor.
    /// </summary>
    public static Task LockAsync(WalletDbContext db, Guid accountId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM accounts WHERE id = {accountId} FOR UPDATE", ct);

    /// <summary>
    /// Bu ay hesabın cüzdanlarına yüklemeyle ve başka hesaplardan transferle gelen. Yükleme
    /// her <see cref="LedgerTransactionType.Topup"/>: havale de kart da; açık kart payları
    /// da yükleme sayılıyor. Transferde işlemin kapsamı gönderen cüzdan; kendi cüzdanından
    /// gelen aktarım o yüzden dışarıda.
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

        var held = await OpenHoldsAsync(db, accountId, currency, ct);

        return new IncomingThisMonth(new Money(deposits + held, currency), new Money(transfers, currency));
    }

    /// <summary>
    /// Hesabın bu para birimindeki bütün cüzdanlarının, bütün kovalarının toplamı; açık
    /// kart payları eklenmiş.
    /// </summary>
    public static async Task<Money> BalanceAsync(
        WalletDbContext db, Guid accountId, Currency currency, CancellationToken ct)
    {
        var walletIds = db.LedgerAccounts
            .Where(a => a.AccountId == accountId)
            .Select(a => a.Id);

        var balance = await db.LedgerBalances
            .Where(b => walletIds.Contains(b.LedgerAccountId) && b.Currency == currency)
            .SumAsync(b => (decimal?)b.Balance, ct) ?? 0m;

        var held = await OpenHoldsAsync(db, accountId, currency, ct);

        return new Money(balance + held, currency);
    }

    /// <summary>Hesabın kapanmamış kart yükleme paylarının toplamı.</summary>
    private static async Task<decimal> OpenHoldsAsync(
        WalletDbContext db, Guid accountId, Currency currency, CancellationToken ct) =>
        await db.CardTopupHolds
            .Where(h => h.AccountId == accountId
                        && h.Currency == currency
                        && !db.CardTopupHoldClosures.Any(c => c.HoldId == h.Id))
            .SumAsync(h => (decimal?)h.Amount, ct) ?? 0m;
}
