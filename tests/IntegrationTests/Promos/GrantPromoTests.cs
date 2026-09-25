using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Promos;

/// <summary>
/// İşyerinin kendi müşterisine promo vermesi (decisions.md madde 37). İşyeri
/// <c>cash</c> kovasından fonluyor, parti yalnızca işyerinin kendisinde geçerli.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GrantPromoTests(PostgresFixture postgres)
{
    private static readonly string Try = SystemAccounts.DefaultCurrencyCode;

    private GrantPromoHandler Handler() =>
        new(postgres.ContextFactory, new SystemClock(), NullLogger<GrantPromoHandler>.Instance);

    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<(Guid Account, Guid Wallet)> WalletAsync(
        AccountType type, decimal funds = 0m, FundType fundType = FundType.Cash)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, type, ct);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Promo", ct);

        if (funds > 0m) await LedgerSeeder.FundAsync(db, wallet, funds, ct, fundType);

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

    [Fact]
    public async Task IsyeriPromoVerir_NakdiDuser_MusteriPromoKovasiArtar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (business, funder) = await WalletAsync(AccountType.Business, funds: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var result = await Handler().HandleAsync(
            new GrantPromoCommand(funder, customer, 100m, Try, expiresAt, Key()), ct);

        result.Replayed.ShouldBeFalse();
        (await BalanceAsync(funder, FundType.Cash)).ShouldBe(400m);
        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(100m);

        await using var db = postgres.CreateContext();
        var grant = await db.PromoGrants.Include(g => g.Merchants).SingleAsync(g => g.Id == result.GrantId, ct);

        grant.LedgerAccountId.ShouldBe(customer);
        grant.Amount.ShouldBe(100m);
        grant.Funder.ShouldBe(PromoFunder.Business);
        grant.FunderLedgerAccountId.ShouldBe(funder);
        grant.Scope.ShouldBe(PromoScope.SelectedBusinesses);
        grant.Merchants.ShouldHaveSingleItem().AccountId.ShouldBe(business, "kapsam fonlayan işyerinin kendisi");
        grant.ExpiresAt.ShouldNotBeNull().ShouldBe(expiresAt, TimeSpan.FromMilliseconds(1));

        var tx = await db.LedgerTransactions.SingleAsync(t => t.Id == grant.LedgerTransactionId, ct);
        tx.Type.ShouldBe(LedgerTransactionType.PromoGrant);
        tx.LedgerAccountId.ShouldBe(funder, "idempotency kapsamı fonlayan cüzdan");
        tx.ActorType.ShouldBe(ActorType.Customer);
        tx.ActorId.ShouldBe(business.ToString(), "hareketi işyeri başlattı");
    }

    [Fact]
    public async Task AyniAnahtarlaTekrar_IkinciPartiAcmaz_AyniPartiyiDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, funder) = await WalletAsync(AccountType.Business, funds: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person);
        var command = new GrantPromoCommand(funder, customer, 100m, Try, null, Key());

        var first = await Handler().HandleAsync(command, ct);
        var second = await Handler().HandleAsync(command, ct);

        second.Replayed.ShouldBeTrue();
        second.GrantId.ShouldBe(first.GrantId);
        (await BalanceAsync(funder, FundType.Cash)).ShouldBe(400m);
        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(100m);
    }

    [Fact]
    public async Task AyniAnahtarlaEszamanliIstekler_TekPartiAcar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, funder) = await WalletAsync(AccountType.Business, funds: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person);
        var command = new GrantPromoCommand(funder, customer, 100m, Try, null, Key());

        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => Handler().HandleAsync(command, ct)));

        results.Select(r => r.GrantId).Distinct().ShouldHaveSingleItem();
        (await BalanceAsync(funder, FundType.Cash)).ShouldBe(400m);
    }

    [Fact]
    public async Task FonlayanKisiyse_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, funder) = await WalletAsync(AccountType.Person, funds: 500m);
        var (_, customer) = await WalletAsync(AccountType.Person);

        await Should.ThrowAsync<PromoGrantRejectedException>(() => Handler().HandleAsync(
            new GrantPromoCommand(funder, customer, 100m, Try, null, Key()), ct));

        (await BalanceAsync(funder, FundType.Cash)).ShouldBe(500m);
    }

    /// <summary>
    /// Kart parası promo'yu fonlayamıyor: fonlayabilseydi işyerinde harcanan promo
    /// işyerinin cash kovasına dönüp IBAN'a çıkardı (decisions.md madde 37).
    /// </summary>
    [Fact]
    public async Task IsyerininYalnizcaKartParasiVarsa_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, funder) = await WalletAsync(AccountType.Business, funds: 500m, fundType: FundType.Card);
        var (_, customer) = await WalletAsync(AccountType.Person);

        await Should.ThrowAsync<InsufficientFundsException>(() => Handler().HandleAsync(
            new GrantPromoCommand(funder, customer, 100m, Try, null, Key()), ct));

        (await BalanceAsync(funder, FundType.Card)).ShouldBe(500m);
        (await BalanceAsync(customer, FundType.Promo)).ShouldBe(0m);
    }

    [Fact]
    public async Task IsyeriKendiHesabinaVeremez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (business, funder) = await WalletAsync(AccountType.Business, funds: 500m);

        Guid ownOther;
        await using (var db = postgres.CreateContext())
        {
            ownOther = await LedgerSeeder.CreateWalletAsync(db, business, "İkinci", ct);
        }

        await Should.ThrowAsync<PromoGrantRejectedException>(() => Handler().HandleAsync(
            new GrantPromoCommand(funder, ownOther, 100m, Try, null, Key()), ct));
    }
}
