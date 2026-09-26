using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Jobs;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Promos;

/// <summary>
/// Süresi dolan partinin kalanının kapatılması (decisions.md madde 37). İşyeri fonlu
/// partinin kalanı işyerinin cash kovasına dönüyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PromoExpiryTests(PostgresFixture postgres)
{
    private static readonly string Try = SystemAccounts.DefaultCurrencyCode;

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private PromoExpirer Expirer() => new(postgres.ContextFactory, NullLogger<PromoExpirer>.Instance);

    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<(Guid Merchant, Guid Customer)> WalletsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = postgres.CreateContext();

        var business = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
        var person = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var merchant = await LedgerSeeder.CreateWalletAsync(db, business, "İşyeri", ct);
        var customer = await LedgerSeeder.CreateWalletAsync(db, person, "Müşteri", ct);

        await LedgerSeeder.FundAsync(db, merchant, 500m, ct);

        return (merchant, customer);
    }

    private async Task<Guid> GrantAsync(Guid merchant, Guid customer, decimal amount, DateTimeOffset? expiresAt)
    {
        var handler = new GrantPromoHandler(
            postgres.ContextFactory, new FixedClock(Now.AddHours(-1)), NullLogger<GrantPromoHandler>.Instance);

        var result = await handler.HandleAsync(
            new GrantPromoCommand(merchant, customer, amount, Try, expiresAt, Key()),
            TestContext.Current.CancellationToken);

        return result.GrantId;
    }

    private async Task PayAsync(Guid from, Guid to, decimal amount)
    {
        var handler = new CreateTransferHandler(
            postgres.ContextFactory,
            new LimitPolicy(new Dictionary<TransferType, TransferLimit>()),
            new CommissionPolicy(new Dictionary<TransferType, CommissionRate>()),
            new FixedClock(Now),
            NullLogger<CreateTransferHandler>.Instance);

        await handler.HandleAsync(
            new CreateTransferCommand(from, to, amount, Try, TransferType.Payment, Key()),
            TestContext.Current.CancellationToken);
    }

    private async Task<decimal> BalanceAsync(Guid wallet, FundType fundType)
    {
        await using var db = postgres.CreateContext();
        return await db.LedgerBalances
            .Where(b => b.LedgerAccountId == wallet && b.FundType == fundType)
            .Select(b => b.Balance)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SuresiDolanParti_KalaniFonlayaninCashKovasinaDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (merchant, customer) = await WalletsAsync();

        var grantId = await GrantAsync(merchant, customer, 100m, expiresAt: Now.AddHours(1));
        await PayAsync(customer, merchant, 30m);

        await Expirer().ExpireDueAsync(Now.AddHours(2), batchSize: 100, ct);

        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(0m);
        (await BalanceAsync(merchant, FundType.Cash)).ShouldBe(500m, "500 - 100 yükleme + 30 ödeme + 70 kalan");

        await using var db = postgres.CreateContext();
        var tx = await db.LedgerTransactions.SingleAsync(
            t => t.LedgerAccountId == customer && t.IdempotencyKey == $"promo-expiry:{grantId}", ct);

        tx.Type.ShouldBe(LedgerTransactionType.PromoExpiry);
        tx.ActorType.ShouldBe(ActorType.System);
        tx.ActorId.ShouldBe("promo-expiry");

        var consumption = await db.PromoConsumptions.SingleAsync(c => c.LedgerTransactionId == tx.Id, ct);
        consumption.GrantId.ShouldBe(grantId);
        consumption.Amount.ShouldBe(70m, "tutar partinin kalanı");
    }

    [Fact]
    public async Task SuresiDolmamisPartiyeDokunmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (merchant, customer) = await WalletsAsync();

        await GrantAsync(merchant, customer, 100m, expiresAt: Now.AddDays(1));
        await GrantAsync(merchant, customer, 50m, expiresAt: null);

        await Expirer().ExpireDueAsync(Now.AddHours(2), batchSize: 100, ct);

        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(150m);
    }

    [Fact]
    public async Task IkinciTur_AyniPartiyiTekrarKapatmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (merchant, customer) = await WalletsAsync();

        var grantId = await GrantAsync(merchant, customer, 100m, expiresAt: Now.AddHours(1));

        await Expirer().ExpireDueAsync(Now.AddHours(2), batchSize: 100, ct);
        await Expirer().ExpireDueAsync(Now.AddHours(3), batchSize: 100, ct);

        await using var db = postgres.CreateContext();
        (await db.PromoConsumptions.CountAsync(c => c.GrantId == grantId, ct)).ShouldBe(1);
        (await BalanceAsync(merchant, FundType.Cash)).ShouldBe(500m);
    }

    [Fact]
    public async Task TamamenHarcanmisParti_KayitUretmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (merchant, customer) = await WalletsAsync();

        var grantId = await GrantAsync(merchant, customer, 100m, expiresAt: Now.AddHours(1));
        await PayAsync(customer, merchant, 100m);

        await Expirer().ExpireDueAsync(Now.AddHours(2), batchSize: 100, ct);

        await using var db = postgres.CreateContext();
        (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == $"promo-expiry:{grantId}", ct))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Platform fonlu partinin kalanı promo_expense'e geri yazılmıyor, promo_breakage
    /// gelir hesabına gidiyor (decisions.md madde 37).
    /// </summary>
    [Fact]
    public async Task PlatformFonluParti_KalaniPromoBreakageHesabinaGider()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, customer) = await WalletsAsync();
        var currency = SystemAccounts.DefaultCurrency;
        var grantedAt = Now.AddHours(-2);

        Guid grantId;
        await using (var db = postgres.CreateContext())
        {
            var campaign = PromoCampaign.Create(
                Guid.NewGuid(), "Süre sonu testi", PromoCampaignRule.DailyPaymentTotal, 1_000_000m,
                PromoRewardType.Fixed, 1m, null, null, currency, PromoScope.AllBusinesses, TimeSpan.FromHours(1),
                1_000m, 1_000m, 1_000m, Now.AddYears(-1), Now.AddYears(-1).AddDays(1), [], [], Now.AddYears(-1));
            db.PromoCampaigns.Add(campaign);

            var tx = LedgerTransaction
                .Create(Guid.NewGuid(), LedgerTransactionType.PromoGrant, customer, SystemActors.PromoCampaign,
                    grantedAt, Key())
                .AddEntry(SystemAccounts.PromoExpenseTry, new Money(-40m, currency), FundType.Promo)
                .AddEntry(customer, new Money(40m, currency), FundType.Promo);
            db.LedgerTransactions.Add(tx);

            var grant = PromoGrant.FromCampaign(Guid.NewGuid(), customer, new Money(40m, currency), campaign, tx.Id, grantedAt);
            db.PromoGrants.Add(grant);
            grantId = grant.Id;

            foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
            {
                var balance = await db.LedgerBalances.SingleAsync(
                    b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == FundType.Promo, ct);
                balance.Apply(entry.Money, canGoNegative: entry.LedgerAccountId != customer, grantedAt);
            }

            await db.SaveChangesAsync(ct);
        }

        await Expirer().ExpireDueAsync(Now, batchSize: 100, ct);

        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(0m);

        await using var verify = postgres.CreateContext();
        var expiry = await verify.LedgerTransactions
            .Include(t => t.Entries)
            .SingleAsync(t => t.IdempotencyKey == $"promo-expiry:{grantId}", ct);

        expiry.Entries.ShouldContain(e => e.LedgerAccountId == SystemAccounts.PromoBreakageTry
                                          && e.Amount == 40m && e.FundType == FundType.Promo);
        expiry.Entries.ShouldNotContain(e => e.LedgerAccountId == SystemAccounts.PromoExpenseTry);
    }
}
