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
/// Kampanya değerlendirmesi (decisions.md madde 37). Ödeme ledger'a yazıldıktan sonra
/// değerlendiriliyor ve kurala uyan hesaba platform fonlu parti açılıyor.
///
/// Veritabanı koleksiyondaki bütün testlerle paylaşılıyor ve değerlendirme her ödemeye
/// bakıyor. Her test kendi zaman penceresinde çalışıyor: kampanyası yalnızca o pencerede
/// geçerli, başka testlerin ödemeleri onu tetiklemiyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PromoCampaignEvaluatorTests(PostgresFixture postgres)
{
    private static readonly string Try = SystemAccounts.DefaultCurrencyCode;

    private static int _window;

    /// <summary>Testin kendi zaman penceresinin başı; pencereler birbirinden 10 gün uzak.</summary>
    private readonly DateTimeOffset _t0 =
        new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(10 * Interlocked.Increment(ref _window));

    private PromoCampaignEvaluator Evaluator() =>
        new(postgres.ContextFactory, NullLogger<PromoCampaignEvaluator>.Instance);

    private Task EvaluateAsync(TimeSpan after) =>
        Evaluator().EvaluateAsync(_t0 + after, lookback: TimeSpan.FromDays(1), batchSize: 1_000,
            TestContext.Current.CancellationToken);

    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<(Guid Account, Guid Wallet)> WalletAsync(AccountType type, decimal cash = 0m)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, type, ct);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Kampanya", ct);

        if (cash > 0m) await LedgerSeeder.FundAsync(db, wallet, cash, ct);

        return (account, wallet);
    }

    private async Task<PromoCampaign> CampaignAsync(
        PromoCampaignRule rule = PromoCampaignRule.PaymentToMerchant,
        Guid[]? triggers = null,
        decimal? threshold = null,
        PromoRewardType rewardType = PromoRewardType.Fixed,
        decimal? amount = 10m,
        decimal? rate = null,
        decimal? max = null,
        decimal budget = 1_000m,
        decimal dailyCap = 100m,
        decimal totalCap = 500m,
        TimeSpan? validFor = null)
    {
        var campaign = PromoCampaign.Create(
            Guid.NewGuid(), "Test kampanyası", rule, threshold, rewardType, amount, rate, max,
            SystemAccounts.DefaultCurrency, PromoScope.AllBusinesses, validFor,
            budget, dailyCap, totalCap,
            startsAt: _t0, endsAt: _t0.AddDays(1),
            triggers ?? [], scopeMerchants: [], _t0);

        await using var db = postgres.CreateContext();
        db.PromoCampaigns.Add(campaign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return campaign;
    }

    private async Task<Guid> PayAsync(Guid from, Guid to, decimal amount, TimeSpan after, CommissionRate? rate = null)
    {
        var handler = new CreateTransferHandler(
            postgres.ContextFactory,
            new LimitPolicy(new Dictionary<TransferType, TransferLimit>()),
            new CommissionPolicy(new Dictionary<TransferType, CommissionRate>
            {
                [TransferType.Payment] = rate ?? CommissionRate.None
            }),
            new FixedClock(_t0 + after),
            NullLogger<CreateTransferHandler>.Instance);

        var result = await handler.HandleAsync(
            new CreateTransferCommand(from, to, amount, Try, TransferType.Payment, Key()),
            TestContext.Current.CancellationToken);

        return result.TransactionId;
    }

    private async Task<List<PromoGrant>> GrantsAsync(Guid campaignId)
    {
        await using var db = postgres.CreateContext();
        return await db.PromoGrants
            .AsNoTracking()
            .Where(g => g.CampaignId == campaignId)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<decimal> PromoAsync(Guid wallet)
    {
        await using var db = postgres.CreateContext();
        return await db.LedgerBalances
            .Where(b => b.LedgerAccountId == wallet && b.FundType == FundType.Promo)
            .Select(b => b.Balance)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IsyerineOdeme_SabitOdul_PlatformFonluPartiAcar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (shop, merchant) = await WalletAsync(AccountType.Business);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 500m);
        var campaign = await CampaignAsync(triggers: [shop], amount: 10m, validFor: TimeSpan.FromDays(7));

        var payment = await PayAsync(customer, merchant, 100m, TimeSpan.FromHours(1));
        await EvaluateAsync(TimeSpan.FromHours(2));

        (await PromoAsync(customer)).ShouldBe(10m);

        var grant = (await GrantsAsync(campaign.Id)).ShouldHaveSingleItem();
        grant.LedgerAccountId.ShouldBe(customer);
        grant.Funder.ShouldBe(PromoFunder.Platform);
        grant.FunderLedgerAccountId.ShouldBeNull();
        grant.Scope.ShouldBe(PromoScope.AllBusinesses);
        grant.ExpiresAt.ShouldBe(grant.CreatedAt.AddDays(7));

        await using var db = postgres.CreateContext();
        var tx = await db.LedgerTransactions.Include(t => t.Entries).SingleAsync(t => t.Id == grant.LedgerTransactionId, ct);
        tx.Type.ShouldBe(LedgerTransactionType.PromoGrant);
        tx.IdempotencyKey.ShouldBe($"campaign:{campaign.Id}:tx:{payment}");
        tx.ActorType.ShouldBe(ActorType.System);
        tx.ActorId.ShouldBe("promo-campaign");
        tx.Entries.ShouldContain(e => e.LedgerAccountId == SystemAccounts.PromoExpenseTry
                                      && e.Amount == -10m && e.FundType == FundType.Promo);
    }

    [Fact]
    public async Task BaskaIsyerineOdeme_Tetiklemez()
    {
        var (shop, _) = await WalletAsync(AccountType.Business);
        var (_, other) = await WalletAsync(AccountType.Business);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 500m);
        var campaign = await CampaignAsync(triggers: [shop]);

        await PayAsync(customer, other, 100m, TimeSpan.FromHours(1));
        await EvaluateAsync(TimeSpan.FromHours(2));

        (await GrantsAsync(campaign.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task KampanyaPenceresiDisindakiOdeme_Tetiklemez()
    {
        var (shop, merchant) = await WalletAsync(AccountType.Business);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 500m);
        var campaign = await CampaignAsync(triggers: [shop]);

        await PayAsync(customer, merchant, 100m, TimeSpan.FromHours(-1));
        await EvaluateAsync(TimeSpan.FromHours(2));

        (await GrantsAsync(campaign.Id)).ShouldBeEmpty();
    }

    /// <summary>
    /// Tabana yalnızca müşterinin kendi parası giriyor: 100 TL'lik ödemenin 40'ı promo,
    /// %5 ödül 60 üzerinden. Komisyon da tabana girmiyor.
    /// </summary>
    [Fact]
    public async Task YuzdeOdul_PromoPayiVeKomisyonTabanaGirmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (shop, merchant) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 500m);

        await new GrantPromoHandler(postgres.ContextFactory, new FixedClock(_t0), NullLogger<GrantPromoHandler>.Instance)
            .HandleAsync(new GrantPromoCommand(merchant, customer, 40m, Try, null, Key()), ct);

        var campaign = await CampaignAsync(
            triggers: [shop], rewardType: PromoRewardType.Percentage, amount: null, rate: 0.05m, max: 100m);

        await PayAsync(customer, merchant, 100m, TimeSpan.FromHours(1), new CommissionRate(0.02m));
        await EvaluateAsync(TimeSpan.FromHours(2));

        (await GrantsAsync(campaign.Id)).ShouldHaveSingleItem().Amount.ShouldBe(3m);
    }

    [Fact]
    public async Task TamamiPromoIleOdeme_OdulKazandirmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (shop, merchant) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person);

        await new GrantPromoHandler(postgres.ContextFactory, new FixedClock(_t0), NullLogger<GrantPromoHandler>.Instance)
            .HandleAsync(new GrantPromoCommand(merchant, customer, 100m, Try, null, Key()), ct);

        var campaign = await CampaignAsync(triggers: [shop]);

        await PayAsync(customer, merchant, 50m, TimeSpan.FromHours(1));
        await EvaluateAsync(TimeSpan.FromHours(2));

        (await GrantsAsync(campaign.Id)).ShouldBeEmpty();
    }

    /// <summary>
    /// Eşik 150: 100 + 60 ödemesinde eşiğe ulaşan ikinci ödeme, sonraki 50 tetiklemiyor.
    /// Hesabın iki cüzdanı olsa da toplam hesap bazında.
    /// </summary>
    [Fact]
    public async Task GunlukEsik_EsigeUlastiranOdemedeBirKezVerir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, merchant) = await WalletAsync(AccountType.Business);
        var (person, first) = await WalletAsync(AccountType.Person, cash: 500m);

        Guid second;
        await using (var db = postgres.CreateContext())
        {
            second = await LedgerSeeder.CreateWalletAsync(db, person, "İkinci", ct);
            await LedgerSeeder.FundAsync(db, second, 500m, ct);
        }

        var campaign = await CampaignAsync(rule: PromoCampaignRule.DailyPaymentTotal, threshold: 150m, amount: 25m);

        await PayAsync(first, merchant, 100m, TimeSpan.FromHours(1));
        var crossing = await PayAsync(second, merchant, 60m, TimeSpan.FromHours(2));
        await PayAsync(first, merchant, 50m, TimeSpan.FromHours(3));
        await EvaluateAsync(TimeSpan.FromHours(4));

        var grant = (await GrantsAsync(campaign.Id)).ShouldHaveSingleItem();
        grant.Amount.ShouldBe(25m);
        grant.LedgerAccountId.ShouldBe(second, "eşiğe ulaştıran ödemeyi yapan cüzdan");

        await using var verify = postgres.CreateContext();
        (await verify.LedgerTransactions.AnyAsync(t => t.Id == grant.LedgerTransactionId
                                                      && t.IdempotencyKey == $"campaign:{campaign.Id}:tx:{crossing}", ct))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task HesapGunlukTavani_OdulKirpilir()
    {
        var (shop, merchant) = await WalletAsync(AccountType.Business);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 500m);
        var campaign = await CampaignAsync(triggers: [shop], amount: 10m, dailyCap: 15m);

        await PayAsync(customer, merchant, 20m, TimeSpan.FromHours(1));
        await PayAsync(customer, merchant, 20m, TimeSpan.FromHours(2));
        await PayAsync(customer, merchant, 20m, TimeSpan.FromHours(3));
        await EvaluateAsync(TimeSpan.FromHours(4));

        (await GrantsAsync(campaign.Id)).Select(g => g.Amount).ShouldBe([10m, 5m]);
    }

    [Fact]
    public async Task Butce_OdulKirpilirVeBitince_ParaVerilmez()
    {
        var (shop, merchant) = await WalletAsync(AccountType.Business);
        var (_, a) = await WalletAsync(AccountType.Person, cash: 500m);
        var (_, b) = await WalletAsync(AccountType.Person, cash: 500m);
        var (_, c) = await WalletAsync(AccountType.Person, cash: 500m);
        var campaign = await CampaignAsync(triggers: [shop], amount: 10m, budget: 15m);

        await PayAsync(a, merchant, 20m, TimeSpan.FromHours(1));
        await PayAsync(b, merchant, 20m, TimeSpan.FromHours(2));
        await PayAsync(c, merchant, 20m, TimeSpan.FromHours(3));
        await EvaluateAsync(TimeSpan.FromHours(4));

        (await GrantsAsync(campaign.Id)).Select(g => g.Amount).ShouldBe([10m, 5m]);
        (await PromoAsync(c)).ShouldBe(0m);
    }

    /// <summary>
    /// Değerlendirilemeyen ödeme turu durdurmuyor. Durdursaydı sıradaki ilk ödeme her
    /// turda aynı yerde hata verir, arkasındakiler lookback'ten düşer ve hiç
    /// değerlendirilmezdi. Bozuk ödeme işaretlenmiyor: sonraki turda yeniden deneniyor.
    /// </summary>
    [Fact]
    public async Task DegerlendirilemeyenOdeme_ArkasindakileriBekletmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (shop, merchant) = await WalletAsync(AccountType.Business);
        var (person, customer) = await WalletAsync(AccountType.Person, cash: 500m);
        var campaign = await CampaignAsync(triggers: [shop], amount: 10m);

        // Alıcı cüzdanı olmayan Payment. API bu kaydı üretmiyor; dengeli olduğu için
        // ledger trigger'ından geçiyor.
        var broken = LedgerTransaction
            .Create(Guid.NewGuid(), LedgerTransactionType.Payment, customer, Actor.Customer(person),
                _t0.AddHours(1), Key())
            .AddEntry(customer, new Money(-10m, SystemAccounts.DefaultCurrency), FundType.Cash)
            .AddEntry(SystemAccounts.ClearingStripeTry, new Money(10m, SystemAccounts.DefaultCurrency), FundType.Cash);

        await using (var db = postgres.CreateContext())
        {
            db.LedgerTransactions.Add(broken);
            await db.SaveChangesAsync(ct);
        }

        await PayAsync(customer, merchant, 100m, TimeSpan.FromHours(2));
        await EvaluateAsync(TimeSpan.FromHours(3));

        (await GrantsAsync(campaign.Id)).ShouldHaveSingleItem();

        await using var verify = postgres.CreateContext();
        (await verify.PromoCampaignEvaluations.AnyAsync(e => e.LedgerTransactionId == broken.Id, ct))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task IkinciDegerlendirme_AyniOdemeyeTekrarVermez()
    {
        var (shop, merchant) = await WalletAsync(AccountType.Business);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 500m);
        var campaign = await CampaignAsync(triggers: [shop], amount: 10m);

        await PayAsync(customer, merchant, 100m, TimeSpan.FromHours(1));
        await EvaluateAsync(TimeSpan.FromHours(2));
        await EvaluateAsync(TimeSpan.FromHours(3));

        (await GrantsAsync(campaign.Id)).ShouldHaveSingleItem();
        (await PromoAsync(customer)).ShouldBe(10m);
    }
}
