using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// Ödemeleri kampanyalara göre değerlendirir ve kurala uyan hesaba platform fonlu parti
/// açar (decisions.md madde 37).
///
/// <b>Kaynak ledger.</b> <c>Payment</c> wallet-api'de yazılıyor ve wallet-api'nin
/// broker bağlantısı yok; değerlendirme ledger'daki işlemlerden okuyor.
///
/// <b>Cursor yok, işaret var.</b> <c>ledger_entries.id</c> sırası commit sırası değil;
/// geç commit olan bir ödeme artan bir cursor'ın gerisinde kalırdı. Son
/// <c>lookback</c> süresindeki değerlendirilmemiş ödemeler okunuyor ve her ödeme,
/// açtığı partilerle aynı transaction'da <c>promo_campaign_evaluations</c>'a işaretleniyor.
///
/// <b>Taban müşterinin kendi parası.</b> Promo ile karşılanan kısım ve komisyon eşik
/// toplamına ve yüzde ödülün tabanına girmiyor: promo ile yapılan ödeme yeni promo
/// kazandırmıyor.
///
/// Ledger: <c>promo_expense</c> <c>-X</c>, müşteri <c>promo</c> <c>+X</c>.
/// </summary>
internal sealed class PromoCampaignEvaluator(
    IDbContextFactory<WalletDbContext> contextFactory,
    ILogger<PromoCampaignEvaluator> logger)
{
    /// <returns>Açılan parti sayısı.</returns>
    public async Task<int> EvaluateAsync(DateTimeOffset now, TimeSpan lookback, int batchSize, CancellationToken ct)
    {
        List<Guid> pending;

        await using (var db = await contextFactory.CreateDbContextAsync(ct))
        {
            var since = now - lookback;

            pending = await db.LedgerTransactions
                .AsNoTracking()
                .Where(t => t.Type == LedgerTransactionType.Payment
                            && t.CreatedAt >= since
                            && !db.PromoCampaignEvaluations.Any(e => e.LedgerTransactionId == t.Id))
                .OrderBy(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Select(t => t.Id)
                .Take(batchSize)
                .ToListAsync(ct);
        }

        var granted = 0;

        foreach (var paymentId in pending)
        {
            try
            {
                granted += await EvaluatePaymentAsync(paymentId, now, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && !IsTransient(exception))
            {
                // Değerlendirilemeyen ödeme turu durdurmuyor. Durdursaydı sıradaki ilk
                // ödeme her turda aynı yerde hata verir, arkasındakiler lookback'ten düşer
                // ve hiç değerlendirilmezdi. İşaret yazılmadı: ödeme her turda yeniden
                // deneniyor ve her denemede bu hata loglanıyor.
                logger.LogError(exception, "Ödeme {PaymentId} kampanyalara göre değerlendirilemedi.", paymentId);
            }
        }

        return granted;
    }

    /// <summary>
    /// Veritabanına şu an ulaşılamıyor: sıradaki ödemeler de aynı hatayı alır. Tur
    /// kesiliyor, <c>ScheduledJob</c> bir kez loglayıp
    /// sonraki turda yeniden deniyor.
    /// </summary>
    private static bool IsTransient(Exception exception) =>
        exception is NpgsqlException { IsTransient: true }
        || exception.InnerException is NpgsqlException { IsTransient: true };

    private async Task<int> EvaluatePaymentAsync(Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var payment = await db.LedgerTransactions
                .AsNoTracking()
                .Include(t => t.Entries)
                .SingleAsync(t => t.Id == paymentId, ct);

            var revenueIds = await db.LedgerAccounts
                .Where(a => a.Type == LedgerAccountType.Revenue)
                .Select(a => a.Id)
                .ToListAsync(ct);

            var involvedIds = payment.Entries.Select(e => e.LedgerAccountId).Distinct().ToArray();
            var accounts = await db.LedgerAccounts
                .AsNoTracking()
                .Where(a => involvedIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);

            // Ödemenin kapsamı gönderen cüzdan (decisions.md madde 15); alıcı, pozitif
            // bacağı olan diğer cüzdan.
            var payer = accounts[payment.LedgerAccountId];
            var merchant = accounts[payment.Entries
                .Where(e => e.Amount > 0m && e.LedgerAccountId != payer.Id
                            && accounts[e.LedgerAccountId].Type is LedgerAccountType.UserWallet)
                .Select(e => e.LedgerAccountId)
                .Distinct()
                .Single()];

            var currency = payer.Currency;
            var ownFunds = OwnFunds(payment, revenueIds);

            var campaigns = await db.PromoCampaigns
                .AsNoTracking()
                .Include(c => c.Merchants)
                .Where(c => c.Currency == currency
                            && c.StartsAt <= payment.CreatedAt
                            && (c.EndsAt == null || payment.CreatedAt < c.EndsAt))
                .ToListAsync(ct);

            var granted = 0;

            foreach (var campaign in campaigns)
            {
                if (!await TriggersAsync(db, campaign, payment, payer, merchant, ownFunds, revenueIds, ct)) continue;

                var reward = await CapAsync(db, campaign, payer, campaign.RewardFor(ownFunds), now, ct);

                if (reward <= 0m) continue;

                var key = $"campaign:{campaign.Id}:tx:{payment.Id}";

                // Aynı ödeme bu kampanyadan zaten parti açtıysa ikinci kez açılmıyor.
                // İşaret ile parti aynı transaction'da yazıldığı için bu durum normalde
                // oluşmuyor; kontrol, işareti olmayan bir partinin sonsuza kadar unique
                // index'e takılmasını engelliyor.
                if (await db.LedgerTransactions.AnyAsync(
                        t => t.LedgerAccountId == payer.Id && t.IdempotencyKey == key, ct)) continue;

                await GrantAsync(db, campaign, payer.Id, new Money(reward, currency), key, now, ct);
                granted++;
            }

            db.PromoCampaignEvaluations.Add(new PromoCampaignEvaluation
            {
                LedgerTransactionId = payment.Id,
                EvaluatedAt = now
            });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return granted;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Müşterinin promo bakiye satırını eşzamanlı bir ödeme değiştirdi. İşaret
            // yazılmadı; sonraki tur aynı ödemeyi yeniden değerlendiriyor.
            logger.LogDebug("Ödeme {PaymentId} bu turda değerlendirilemedi: eşzamanlı yazım.", paymentId);
            return 0;
        }
    }

    private static async Task<bool> TriggersAsync(
        WalletDbContext db,
        PromoCampaign campaign,
        LedgerTransaction payment,
        LedgerAccount payer,
        LedgerAccount merchant,
        decimal ownFunds,
        IReadOnlyCollection<Guid> revenueIds,
        CancellationToken ct)
    {
        // Promo ile yapılan ödeme yeni promo kazandırmıyor.
        if (ownFunds <= 0m) return false;

        switch (campaign.Rule)
        {
            case PromoCampaignRule.PaymentToMerchant:
                return merchant.AccountId is { } merchantAccountId && campaign.TriggerMerchants.Contains(merchantAccountId);

            case PromoCampaignRule.DailyPaymentTotal:
            {
                // Hesabın o günkü (UTC) ödemeleri, bu ödemeye kadar. Toplam yalnızca
                // artıyor ve ödeme iadesi yok: eşiğe ulaştıran ödeme tek.
                var dayStart = new DateTimeOffset(payment.CreatedAt.UtcDateTime.Date, TimeSpan.Zero);
                var walletIds = db.LedgerAccounts.Where(a => a.AccountId == payer.AccountId).Select(a => a.Id);

                var dayPayments = await db.LedgerTransactions
                    .AsNoTracking()
                    .Include(t => t.Entries)
                    .Where(t => t.Type == LedgerTransactionType.Payment
                                && walletIds.Contains(t.LedgerAccountId)
                                && t.CreatedAt >= dayStart
                                && t.CreatedAt <= payment.CreatedAt)
                    .ToListAsync(ct);

                var before = dayPayments
                    .Where(t => t.CreatedAt < payment.CreatedAt
                                || (t.CreatedAt == payment.CreatedAt && t.Id.CompareTo(payment.Id) < 0))
                    .Sum(t => OwnFunds(t, revenueIds));

                var threshold = campaign.ThresholdAmount!.Value;

                return before < threshold && before + ownFunds >= threshold;
            }

            default:
                throw new InvalidOperationException($"Bilinmeyen kampanya kuralı: {campaign.Rule}.");
        }
    }

    /// <summary>
    /// Ödülü üç sınırın kalanından en küçüğüne kırpar: kampanya bütçesi, hesabın bu
    /// kampanyadan bugün (partinin açıldığı UTC günü) aldığı, hesabın kampanya boyunca aldığı.
    /// </summary>
    private static async Task<decimal> CapAsync(
        WalletDbContext db, PromoCampaign campaign, LedgerAccount payer, decimal reward, DateTimeOffset now,
        CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var walletIds = db.LedgerAccounts.Where(a => a.AccountId == payer.AccountId).Select(a => a.Id);
        var grants = db.PromoGrants.Where(g => g.CampaignId == campaign.Id);

        var spent = await grants.SumAsync(g => (decimal?)g.Amount, ct) ?? 0m;
        var toAccount = await grants
            .Where(g => walletIds.Contains(g.LedgerAccountId))
            .SumAsync(g => (decimal?)g.Amount, ct) ?? 0m;
        var toAccountToday = await grants
            .Where(g => walletIds.Contains(g.LedgerAccountId) && g.CreatedAt >= dayStart)
            .SumAsync(g => (decimal?)g.Amount, ct) ?? 0m;

        return Math.Min(reward, Math.Min(campaign.Budget - spent,
            Math.Min(campaign.TotalCapPerAccount - toAccount, campaign.DailyCapPerAccount - toAccountToday)));
    }

    private static async Task GrantAsync(
        WalletDbContext db, PromoCampaign campaign, Guid walletId, Money reward, string key, DateTimeOffset now,
        CancellationToken ct)
    {
        var tx = LedgerTransaction
            .Create(Guid.NewGuid(), LedgerTransactionType.PromoGrant, walletId, SystemActors.PromoCampaign, now, key)
            .AddEntry(SystemAccounts.PromoExpenseTry, reward.Negated, FundType.Promo)
            .AddEntry(walletId, reward, FundType.Promo);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        db.PromoGrants.Add(PromoGrant.FromCampaign(Guid.NewGuid(), walletId, reward, campaign, tx.Id, now));

        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8). promo_expense
        // sistem hesabı, negatife düşebiliyor.
        foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
        {
            var balance = await db.LedgerBalances.SingleAsync(
                b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct);

            balance.Apply(entry.Money, canGoNegative: entry.LedgerAccountId != walletId, now);
        }
    }

    /// <summary>
    /// Ödemenin müşterinin kendi parasıyla (card, cash) karşılanan kısmı, komisyon hariç:
    /// gönderenin promo dışı bacakları eksi komisyon bacakları.
    /// </summary>
    private static decimal OwnFunds(LedgerTransaction payment, IReadOnlyCollection<Guid> revenueIds)
    {
        var paid = -payment.Entries
            .Where(e => e.LedgerAccountId == payment.LedgerAccountId && e.FundType is not FundType.Promo)
            .Sum(e => e.Amount);

        var commission = payment.Entries
            .Where(e => revenueIds.Contains(e.LedgerAccountId))
            .Sum(e => e.Amount);

        return paid - commission;
    }
}
