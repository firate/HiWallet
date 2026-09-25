using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// Süresi dolan partilerin kalanını kapatır (decisions.md madde 37). Her parti ayrı
/// bir ledger işlemi: biri patlarsa diğerleri yine kapanıyor.
///
/// Ledger: müşteri <c>promo</c> <c>-kalan</c>, fonlayan işyeri <c>cash</c> <c>+kalan</c>.
/// Bacaklar partinin kayıtlı fonlayanından okunuyor, tarifeden üretilmiyor.
///
/// Mutabakat işinden farklı olarak ledger'a yazıyor: süre sonu kayıtlı verilerden
/// kesin olarak çıkan bir olay, tahmin değil.
/// </summary>
internal sealed class PromoExpirer(
    IDbContextFactory<WalletDbContext> contextFactory,
    ILogger<PromoExpirer> logger)
{
    /// <returns>Kapatılan parti sayısı.</returns>
    public async Task<int> ExpireDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct)
    {
        List<Guid> due;

        await using (var db = await contextFactory.CreateDbContextAsync(ct))
        {
            due = await db.PromoGrants
                .AsNoTracking()
                .Where(g => g.ExpiresAt != null
                            && g.ExpiresAt <= now
                            && g.Amount > (db.PromoConsumptions
                                .Where(c => c.GrantId == g.Id)
                                .Sum(c => (decimal?)c.Amount) ?? 0m))
                .OrderBy(g => g.ExpiresAt)
                .Select(g => g.Id)
                .Take(batchSize)
                .ToListAsync(ct);
        }

        var expired = 0;

        foreach (var grantId in due)
        {
            if (await ExpireAsync(grantId, now, ct)) expired++;
        }

        return expired;
    }

    private async Task<bool> ExpireAsync(Guid grantId, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var grant = await db.PromoGrants.AsNoTracking().SingleAsync(g => g.Id == grantId, ct);

            var funderWallet = grant.FunderLedgerAccountId
                               ?? throw new InvalidOperationException(
                                   $"Parti {grant.Id} platform fonlu; promo_breakage hesabı henüz yok.");

            // Bakiye satırı tüketimlerden ÖNCE okunuyor. Arada commit olan bir ödeme
            // tüketimi artırırsa elimizdeki version eskimiş olur ve kayıt reddedilir;
            // ters sırada eski tüketimle hesaplanan fazla kalan yeni version'la geçerdi.
            var promoBalance = await db.LedgerBalances.SingleAsync(
                b => b.LedgerAccountId == grant.LedgerAccountId && b.FundType == FundType.Promo, ct);

            var consumed = await db.PromoConsumptions
                .Where(c => c.GrantId == grant.Id)
                .SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;

            var remaining = new Money(grant.Amount - consumed, grant.Currency);

            if (remaining.Amount <= 0m) return false;

            var tx = LedgerTransaction
                .Create(Guid.NewGuid(), LedgerTransactionType.PromoExpiry, grant.LedgerAccountId,
                    SystemActors.PromoExpiry, now, $"promo-expiry:{grant.Id}")
                .AddEntry(grant.LedgerAccountId, remaining.Negated, FundType.Promo)
                .AddEntry(funderWallet, remaining, FundType.Cash);

            tx.AssertBalanced();
            db.LedgerTransactions.Add(tx);
            db.PromoConsumptions.Add(new PromoConsumption(grant.Id, tx.Id, remaining.Amount, now));

            var funderBalance = await db.LedgerBalances.SingleAsync(
                b => b.LedgerAccountId == funderWallet && b.FundType == FundType.Cash, ct);

            // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8).
            foreach (var (balance, delta) in new[] { (promoBalance, remaining.Negated), (funderBalance, remaining) }
                         .OrderBy(x => x.Item1.LedgerAccountId))
            {
                balance.Apply(delta, canGoNegative: false, now);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Aynı cüzdanın promo'sunu eşzamanlı bir ödeme değiştirdi. Sonraki tur
            // yeni kalanla yeniden deniyor.
            logger.LogDebug("Promo partisi {GrantId} bu turda kapatılamadı: eşzamanlı yazım.", grantId);
            return false;
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            logger.LogDebug("Promo partisi {GrantId} başka bir turda kapatıldı.", grantId);
            return false;
        }
        catch (InsufficientFundsException exception)
        {
            // Promo bakiyesi partinin kalanını karşılamıyor: bakiye ile partiler
            // ayrışmış. Parti atlanıyor ki diğerleri kapanabilsin; mutabakat aynı
            // ayrışmayı Error olarak raporluyor.
            logger.LogError(exception,
                "Promo partisi {GrantId} kapatılamadı: cüzdanın promo bakiyesi partinin kalanından az.", grantId);
            return false;
        }
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "ux_ledger_tx_idem";
}
