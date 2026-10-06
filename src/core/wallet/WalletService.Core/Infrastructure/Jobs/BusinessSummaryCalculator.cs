using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// Bir günün işletme özetlerini hesaplayıp <c>business_daily_summaries</c>'e yazar
/// (<c>overview.md</c> madde 7).
///
/// Zamanlamadan ayrı duruyor: test bir saat beklemek zorunda kalmasın ve hesabın
/// kendisi doğrudan sınanabilsin.
/// </summary>
internal sealed class BusinessSummaryCalculator(
    IDbContextFactory<WalletDbContext> contextFactory,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Cirodan sayılan işlem tipleri. Top-up ve iade DIŞARIDA: ikisi de işletmenin
    /// cüzdanına para GİRDİRİYOR ama satış değil — biri kendi para yüklemesi, öbürü
    /// gerçekleşmemiş bir çekimin geri dönüşü. Sayılsalardı ciro, işletme kendi
    /// parasını cüzdana koydukça şişerdi.
    /// </summary>
    private static readonly LedgerTransactionType[] Counted =
    [
        LedgerTransactionType.Payment,
        LedgerTransactionType.P2B,
        LedgerTransactionType.B2B
    ];

    public async Task<int> CalculateAsync(DateOnly day, CancellationToken ct)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // İşletme cüzdanlarına GİREN bacaklar. Tek sorguda komisyonu da toplamak
        // istemedim: komisyon işlem başına duruyor, bacak başına değil — aynı
        // gruplamada toplansaydı işletmenin iki bacaklı bir işleminde iki kez
        // sayılırdı.
        var receipts =
            from entry in db.LedgerEntries
            join wallet in db.LedgerAccounts on entry.LedgerAccountId equals wallet.Id
            join account in db.Accounts on wallet.AccountId equals account.Id
            join tx in db.LedgerTransactions on entry.TransactionId equals tx.Id
            where account.Type == AccountType.Business
                  && entry.Amount > 0m
                  && entry.CreatedAt >= start
                  && entry.CreatedAt < end
                  && Counted.Contains(tx.Type)
            select new
            {
                AccountId = account.Id,
                entry.Currency,
                entry.TransactionId,
                entry.Amount
            };

        var volumes = await receipts
            .GroupBy(row => new { row.AccountId, row.Currency })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.Currency,
                Volume = g.Sum(row => row.Amount),
                TransactionCount = g.Select(row => row.TransactionId).Distinct().Count()
            })
            .ToListAsync(ct);

        if (volumes.Count == 0)
        {
            return 0;
        }

        // Komisyon: aynı işlemlerin revenue bacakları. Parayı müşteri ödedi ama
        // geliri bu işletmenin cirosu üretti — rapor onu işletmeye yazıyor.
        var commissions = await (
                from revenue in db.LedgerEntries
                join account in db.LedgerAccounts on revenue.LedgerAccountId equals account.Id
                join receipt in receipts on revenue.TransactionId equals receipt.TransactionId
                where account.Type == LedgerAccountType.Revenue
                select new { receipt.AccountId, revenue.Currency, revenue.Amount })
            .GroupBy(row => new { row.AccountId, row.Currency })
            .Select(g => new { g.Key.AccountId, g.Key.Currency, Commission = g.Sum(row => row.Amount) })
            .ToListAsync(ct);

        var commissionByKey = commissions.ToDictionary(
            row => (row.AccountId, row.Currency), row => row.Commission);

        var now = timeProvider.GetUtcNow();

        foreach (var row in volumes)
        {
            var currency = row.Currency.Code;

            // Upsert: kapalı bir gün yeniden hesaplandığında ikinci satır değil,
            // aynı satırın güncellenmesi gerekiyor. Anahtar (hesap, gün, currency).
            var existing = await db.BusinessDailySummaries
                .FirstOrDefaultAsync(
                    s => s.AccountId == row.AccountId && s.Day == day && s.Currency == currency, ct);

            if (existing is null)
            {
                existing = new BusinessDailySummary
                {
                    AccountId = row.AccountId,
                    Day = day,
                    Currency = currency
                };

                db.BusinessDailySummaries.Add(existing);
            }

            existing.Volume = row.Volume;
            existing.TransactionCount = row.TransactionCount;
            existing.Commission = commissionByKey.GetValueOrDefault((row.AccountId, row.Currency), 0m);
            existing.CalculatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return volumes.Count;
    }
}
