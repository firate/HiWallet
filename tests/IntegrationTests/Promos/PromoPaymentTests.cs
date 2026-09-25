using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Promos;

/// <summary>
/// Promo'nun ödemede harcanması (decisions.md madde 37). Promo yalnızca
/// <c>Payment</c>'ta, yalnızca geçerli olduğu işyerinde ve yalnızca tutar için
/// kullanılıyor; işyerine <c>cash</c> olarak geçiyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PromoPaymentTests(PostgresFixture postgres)
{
    private static readonly string Try = SystemAccounts.DefaultCurrencyCode;

    private CreateTransferHandler Payments(CommissionRate? rate = null, IClock? clock = null) =>
        new(postgres.ContextFactory,
            new LimitPolicy(new Dictionary<TransferType, TransferLimit>()),
            new CommissionPolicy(new Dictionary<TransferType, CommissionRate>
            {
                [TransferType.Payment] = rate ?? CommissionRate.None
            }),
            clock ?? new SystemClock(),
            NullLogger<CreateTransferHandler>.Instance);

    private GrantPromoHandler Grants(IClock? clock = null) =>
        new(postgres.ContextFactory, clock ?? new SystemClock(), NullLogger<GrantPromoHandler>.Instance);

    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<(Guid Account, Guid Wallet)> WalletAsync(AccountType type, decimal cash = 0m)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, type, ct);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Promo ödeme", ct);

        if (cash > 0m) await LedgerSeeder.FundAsync(db, wallet, cash, ct);

        return (account, wallet);
    }

    private async Task<decimal> BalanceAsync(Guid wallet, FundType fundType)
    {
        await using var db = postgres.CreateContext();
        return await db.LedgerBalances
            .Where(b => b.LedgerAccountId == wallet && b.FundType == fundType)
            .Select(b => b.Balance)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private Task<TransferResult> PayAsync(Guid from, Guid to, decimal amount, CreateTransferHandler? handler = null) =>
        (handler ?? Payments()).HandleAsync(
            new CreateTransferCommand(from, to, amount, Try, TransferType.Payment, Key()),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Odeme_IsyerindeGecerliPromoyuKullanir_IsyerineCashGecer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, merchant) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 200m);

        var grant = await Grants().HandleAsync(
            new GrantPromoCommand(merchant, customer, 100m, Try, null, Key()), ct);

        var payment = await PayAsync(customer, merchant, 150m);

        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(0m);
        (await BalanceAsync(customer, FundType.Cash)).ShouldBe(150m, "tutarın kalanı nakitten");
        (await BalanceAsync(merchant, FundType.Cash)).ShouldBe(550m, "400 + ödemenin tamamı cash");
        (await BalanceAsync(merchant, FundType.Promo)).ShouldBe(0m, "işyerine promo geçmez");

        await using var db = postgres.CreateContext();
        var consumption = await db.PromoConsumptions.SingleAsync(c => c.GrantId == grant.GrantId, ct);
        consumption.LedgerTransactionId.ShouldBe(payment.TransactionId);
        consumption.Amount.ShouldBe(100m);
    }

    [Fact]
    public async Task Odeme_BaskaIsyerindeGecerliPromoyuKullanmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, issuer) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, other) = await WalletAsync(AccountType.Business);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 200m);

        await Grants().HandleAsync(new GrantPromoCommand(issuer, customer, 100m, Try, null, Key()), ct);

        await PayAsync(customer, other, 50m);

        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(100m);
        (await BalanceAsync(customer, FundType.Cash)).ShouldBe(150m);
    }

    /// <summary>
    /// Toplam bakiye yetse bile bu işyerinde geçerli promo + nakit yetmiyorsa ret.
    /// </summary>
    [Fact]
    public async Task Odeme_BaskaIsyerininPromosuSayilmaz_YetersizBakiye()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, issuer) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, other) = await WalletAsync(AccountType.Business);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 20m);

        await Grants().HandleAsync(new GrantPromoCommand(issuer, customer, 100m, Try, null, Key()), ct);

        await Should.ThrowAsync<InsufficientFundsException>(() => PayAsync(customer, other, 50m));
    }

    [Fact]
    public async Task Odeme_KomisyonPromodanOdenmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, merchant) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 10m);

        await Grants().HandleAsync(new GrantPromoCommand(merchant, customer, 100m, Try, null, Key()), ct);

        await PayAsync(customer, merchant, 100m, Payments(rate: new CommissionRate(0.02m)));

        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(0m);
        (await BalanceAsync(customer, FundType.Cash)).ShouldBe(8m, "komisyon 2 nakitten");
        (await BalanceAsync(merchant, FundType.Cash)).ShouldBe(500m, "400 + 100");
    }

    [Fact]
    public async Task Odeme_SuresiDolmusPartiyiKullanmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow.AddDays(-10);
        var (_, merchant) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 200m);

        await Grants(new FixedClock(t0)).HandleAsync(
            new GrantPromoCommand(merchant, customer, 100m, Try, t0.AddDays(1), Key()), ct);

        await PayAsync(customer, merchant, 50m);

        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(100m, "süresi dolmuş parti ödemeye girmez");
        (await BalanceAsync(customer, FundType.Cash)).ShouldBe(150m);
    }

    [Fact]
    public async Task Odeme_BitisiYakinPartiOnceTuketilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, merchant) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person);

        var later = await Grants().HandleAsync(new GrantPromoCommand(
            merchant, customer, 50m, Try, DateTimeOffset.UtcNow.AddDays(30), Key()), ct);
        var sooner = await Grants().HandleAsync(new GrantPromoCommand(
            merchant, customer, 50m, Try, DateTimeOffset.UtcNow.AddDays(5), Key()), ct);

        await PayAsync(customer, merchant, 60m);

        await using var db = postgres.CreateContext();
        var consumed = await db.PromoConsumptions
            .Where(c => c.GrantId == later.GrantId || c.GrantId == sooner.GrantId)
            .ToDictionaryAsync(c => c.GrantId, c => c.Amount, ct);

        consumed[sooner.GrantId].ShouldBe(50m);
        consumed[later.GrantId].ShouldBe(10m);
    }

    /// <summary>
    /// Aynı partiyi eşzamanlı ödemeler paylaşıyor. Cüzdanın promo bakiye satırının
    /// version'ı yazanları sıraya sokuyor: hiçbir parti tutarından fazla tüketilmez ve
    /// promo bakiyesi partilerin kalanlarının toplamına eşit kalır.
    /// </summary>
    [Fact]
    public async Task EszamanliOdemeler_PartiyiTutarindanFazlaTuketmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, merchant) = await WalletAsync(AccountType.Business, cash: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person, cash: 1_000m);

        var grant = await Grants().HandleAsync(
            new GrantPromoCommand(merchant, customer, 100m, Try, null, Key()), ct);

        var handler = Payments();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            try
            {
                await PayAsync(customer, merchant, 30m, handler);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Retry tükendi → 409. Yazılan bir şey yok; iddia invariant üzerine.
            }
        }));

        await using var db = postgres.CreateContext();
        var consumed = await db.PromoConsumptions
            .Where(c => c.GrantId == grant.GrantId)
            .SumAsync(c => c.Amount, ct);

        consumed.ShouldBeLessThanOrEqualTo(100m);
        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(100m - consumed);
    }
}
