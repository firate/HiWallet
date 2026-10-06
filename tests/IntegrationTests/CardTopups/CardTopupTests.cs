using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.CardTopups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.CardTopups;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.CardTopups;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.CardTopups;

/// <summary>
/// Kartla yüklemenin seviye limiti. Kart limitte öncelikli: ödeme başlarken limit
/// kontrol ediliyor ve tutar ayrılıyor; ödeme sürerken hesaba gelen başka para (havale,
/// transfer, ikinci bir kart yüklemesi) o payla birlikte sayılıyor; ödeme kapanınca pay
/// düşüyor. Broker gerekmiyor: mesajlar doğrudan handler'lara veriliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardTopupTests(PostgresFixture postgres)
{
    private const string Provider = "stripe-fake";

    private static readonly Currency Try = SystemAccounts.DefaultCurrency;

    private PlaceCardTopupHoldHandler PlaceHandler(IClock? clock = null) => new(
        postgres.ContextFactory,
        TestKycLimits.Policy,
        clock ?? new SystemClock(),
        NullLogger<PlaceCardTopupHoldHandler>.Instance);

    private ProcessCardTopupHandler CloseHandler() => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new SystemClock(),
        NullLogger<ProcessCardTopupHandler>.Instance);

    private static PlaceCardTopupHoldCommand Place(Guid wallet, decimal amount, Guid? id = null, string currency = "TRY") =>
        new(id ?? Guid.NewGuid(), wallet, amount, currency, Provider);

    private static CardTopupClosed Closed(Guid id, string outcome, decimal amount, string currency = "TRY") => new()
    {
        CardTopupId = id,
        Outcome = outcome,
        Provider = Provider,
        ProviderRef = $"pay_{id:N}"[..16],
        Amount = amount,
        Currency = currency,
        ClosedAt = DateTimeOffset.UtcNow
    };

    private async Task<(Guid Account, Guid Wallet)> CustomerAsync(
        CancellationToken ct, KycLevel level = KycLevel.Unverified, decimal funds = 0m)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, level);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);

        if (funds > 0m)
        {
            await LedgerSeeder.FundAsync(db, wallet, funds, ct);
        }

        return (account, wallet);
    }

    private async Task<int> HoldCountAsync(Guid account, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await db.CardTopupHolds.CountAsync(h => h.AccountId == account, ct);
    }

    private async Task<decimal> BucketAsync(Guid wallet, FundType fundType, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await db.LedgerBalances
            .Where(b => b.LedgerAccountId == wallet && b.FundType == fundType)
            .Select(b => b.Balance)
            .SingleAsync(ct);
    }

    // ------------------------------------------------------------------
    // Başlangıç: limit kontrolü ve pay
    // ------------------------------------------------------------------
    [Fact]
    public async Task Baslangic_LimitinAltinda_PayiAyirir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var command = Place(customer.Wallet, 1_000m);

        var result = await PlaceHandler().HandleAsync(command, ct);

        result.Replayed.ShouldBeFalse();
        result.AccountId.ShouldBe(customer.Account);
        result.Amount.Amount.ShouldBe(1_000m);

        await using var db = postgres.CreateContext();
        var hold = await db.CardTopupHolds.SingleAsync(h => h.Id == command.HoldId, ct);

        hold.WalletId.ShouldBe(customer.Wallet);
        hold.Provider.ShouldBe(Provider);

        // Para henüz hareket etmedi: pay ledger'a yazılmıyor.
        (await BucketAsync(customer.Wallet, FundType.Card, ct)).ShouldBe(0m);
    }

    /// <summary>Limit yetmiyorsa ödeme hiç açılmıyor: pay yazılmıyor, kart çekilmiyor.</summary>
    [Fact]
    public async Task Baslangic_LimitiAsiyorsa_Reddeder_PayYazmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);

        var error = await Should.ThrowAsync<CardTopupLimitExceededException>(
            () => PlaceHandler().HandleAsync(Place(customer.Wallet, 5_500.01m), ct));

        error.Limit.Amount.ShouldBe(5_500m);
        (await HoldCountAsync(customer.Account, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Baslangic_DogrulanmamisHesap_Reddeder()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct, KycLevel.Unknown);

        await Should.ThrowAsync<CardTopupLimitExceededException>(
            () => PlaceHandler().HandleAsync(Place(customer.Wallet, 10m), ct));
    }

    /// <summary>Bakiye tavanı: cüzdandaki para ve açık pay birlikte sayılıyor.</summary>
    [Fact]
    public async Task Baslangic_BakiyeVeAcikPayBirlikteTavaniAsarsa_Reddeder()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct, funds: 3_000m);

        await PlaceHandler().HandleAsync(Place(customer.Wallet, 2_000m), ct);

        await Should.ThrowAsync<CardTopupLimitExceededException>(
            () => PlaceHandler().HandleAsync(Place(customer.Wallet, 600m), ct));

        // Tam tavana kadar olan geçiyor.
        await PlaceHandler().HandleAsync(Place(customer.Wallet, 500m), ct);
    }

    [Fact]
    public async Task Baslangic_IsyeriHesabi_Reddeder()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid wallet;

        await using (var db = postgres.CreateContext())
        {
            var shop = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            wallet = await LedgerSeeder.CreateWalletAsync(db, shop, "Dükkan", ct);
        }

        await Should.ThrowAsync<AccountRuleException>(
            () => PlaceHandler().HandleAsync(Place(wallet, 10m), ct));
    }

    [Fact]
    public async Task Baslangic_CuzdaninParaBirimindenFarkli_Reddeder()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);

        await Should.ThrowAsync<AccountRuleException>(
            () => PlaceHandler().HandleAsync(Place(customer.Wallet, 10m, currency: "USD"), ct));
    }

    [Fact]
    public async Task Baslangic_OlmayanCuzdan_404()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<WalletNotFoundException>(
            () => PlaceHandler().HandleAsync(Place(Guid.NewGuid(), 10m), ct));
    }

    /// <summary>
    /// Aynı kimlikle tekrar aynı payı döner ve limit yeniden değerlendirilmez: kendi payı
    /// onu limitin dışına iterse tekrar eden istek reddedilirdi.
    /// </summary>
    [Fact]
    public async Task Baslangic_AyniKimlik_AyniPayiDoner_LimitiYenidenSormaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var command = Place(customer.Wallet, 5_500m);

        var first = await PlaceHandler().HandleAsync(command, ct);
        var second = await PlaceHandler().HandleAsync(command, ct);

        first.Replayed.ShouldBeFalse();
        second.Replayed.ShouldBeTrue();
        (await HoldCountAsync(customer.Account, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Baslangic_AyniKimlikBaskaTutar_Hata()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var id = Guid.NewGuid();

        await PlaceHandler().HandleAsync(Place(customer.Wallet, 100m, id), ct);

        await Should.ThrowAsync<AccountRuleException>(
            () => PlaceHandler().HandleAsync(Place(customer.Wallet, 200m, id), ct));
    }

    /// <summary>
    /// Aynı hesaba aynı anda başlayan iki yükleme: ikisi de limiti boş görürse tavan
    /// aşılırdı. Hesabın satırı kilitleniyor, biri sıraya giriyor ve ötekinin payını görüyor.
    /// </summary>
    [Fact]
    public async Task Baslangic_EsZamanliIkiYukleme_TavanAsilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);

        var attempts = await Task.WhenAll(
            Attempt(Place(customer.Wallet, 3_000m), ct),
            Attempt(Place(customer.Wallet, 3_000m), ct));

        attempts.Count(placed => placed).ShouldBe(1);
        (await HoldCountAsync(customer.Account, ct)).ShouldBe(1);
    }

    private async Task<bool> Attempt(PlaceCardTopupHoldCommand command, CancellationToken ct)
    {
        try
        {
            await PlaceHandler().HandleAsync(command, ct);
            return true;
        }
        catch (CardTopupLimitExceededException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Açık pay hesaba gelen diğer parayı engelliyor
    // ------------------------------------------------------------------
    private ProcessDepositHandler DepositHandler(FakeHolderIdentity holders) => new(
        postgres.ContextFactory,
        holders,
        TestKycLimits.Policy,
        new SystemClock(),
        NullLogger<ProcessDepositHandler>.Instance);

    private async Task<(string Number, FakeHolderIdentity Holders)> DepositTargetAsync(Guid account, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        var number = (await db.Accounts.Where(a => a.Id == account).Select(a => a.Number).SingleAsync(ct)).Value;

        var holders = new FakeHolderIdentity();
        holders.Verified(TestTokens.SubjectOf(account), "10000000146");

        return (number, holders);
    }

    private static HiWallet.Shared.Contracts.Deposits.BankDepositReceived Deposit(string number, decimal amount) => new()
    {
        Provider = SystemAccounts.BankFake,
        BankReference = $"GLN{Guid.NewGuid():N}"[..19].ToUpperInvariant(),
        Amount = amount,
        Currency = "TRY",
        Description = number,
        SenderNationalId = "10000000146",
        ReceivedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task AcikPay_HavaleyiAskiyaDusurur_KapaninceYineKabulEder()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var (number, holders) = await DepositTargetAsync(customer.Account, ct);
        var hold = Place(customer.Wallet, 4_000m);

        await PlaceHandler().HandleAsync(hold, ct);

        // 4.000 ayrılmış; 2.000'lik havale tavanı (5.500) aşıyor.
        var blocked = await DepositHandler(holders).HandleAsync(Deposit(number, 2_000m), ct);
        blocked.HeldFor.ShouldBe(DepositHoldReason.LimitExceeded);

        // Yükleme ödenmeden kapandı: pay serbest, havale artık geçiyor.
        await CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Failed, 4_000m), ct);

        var accepted = await DepositHandler(holders).HandleAsync(Deposit(number, 2_000m), ct);
        accepted.HeldFor.ShouldBeNull();
    }

    [Fact]
    public async Task AcikPay_GelenTransferiReddeder()
    {
        var ct = TestContext.Current.CancellationToken;
        var receiver = await CustomerAsync(ct);
        Guid sender;

        await using (var db = postgres.CreateContext())
        {
            var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, KycLevel.Contracted);
            sender = await LedgerSeeder.CreateWalletAsync(db, account, "Gönderen", ct);
            await LedgerSeeder.FundAsync(db, sender, 10_000m, ct);
        }

        await PlaceHandler().HandleAsync(Place(receiver.Wallet, 4_000m), ct);

        var transfers = new CreateTransferHandler(
            postgres.ContextFactory,
            new LimitPolicy(new Dictionary<TransferType, TransferLimit>()),
            TestKycLimits.Policy,
            new CommissionPolicy(new Dictionary<TransferType, CommissionRate>()),
            new SystemClock(),
            NullLogger<CreateTransferHandler>.Instance);

        await Should.ThrowAsync<IncomingLimitExceededException>(() => transfers.HandleAsync(
            new CreateTransferCommand(sender, receiver.Wallet, 2_000m, "TRY", TransferType.P2P, Guid.NewGuid().ToString("N")), ct));

        // Pay dışında kalan tutar geçiyor.
        await transfers.HandleAsync(
            new CreateTransferCommand(sender, receiver.Wallet, 1_500m, "TRY", TransferType.P2P, Guid.NewGuid().ToString("N")), ct);
    }

    [Fact]
    public async Task AcikPay_IkinciKartYuklemesiniEngeller()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);

        await PlaceHandler().HandleAsync(Place(customer.Wallet, 4_000m), ct);

        await Should.ThrowAsync<CardTopupLimitExceededException>(
            () => PlaceHandler().HandleAsync(Place(customer.Wallet, 2_000m), ct));
    }

    /// <summary>
    /// Pay saatle düşmüyor, kapanışı bekliyor: süreyi saat değil sağlayıcı söylüyor. Saatle
    /// düşseydi son anda çekilen kartın bildirimi geciktiğinde arada gelen başka para tavanı
    /// doldurur ve kartın önceliği delinirdi.
    /// </summary>
    [Fact]
    public async Task AcikPay_SaatGecseDeKapanisaKadarSayilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var hold = Place(customer.Wallet, 5_000m);

        await PlaceHandler(new FixedClock(now)).HandleAsync(hold, ct);

        await Should.ThrowAsync<CardTopupLimitExceededException>(
            () => PlaceHandler(new FixedClock(now.AddDays(1))).HandleAsync(Place(customer.Wallet, 1_000m), ct));

        await CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Failed, 5_000m), ct);

        await PlaceHandler(new FixedClock(now.AddDays(1))).HandleAsync(Place(customer.Wallet, 1_000m), ct);
    }

    // ------------------------------------------------------------------
    // Kapanış: ödendi, ödenmedi
    // ------------------------------------------------------------------
    [Fact]
    public async Task Odendi_ParaKartKovasinaYazilir_ClearingEksilir_UcretBeklentisiYazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 250m);
        await PlaceHandler().HandleAsync(hold, ct);

        var result = await CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Paid, 250m), ct);

        result.Outcome.ShouldBe(CardTopupOutcome.Paid);
        result.Replayed.ShouldBeFalse();

        await using var db = postgres.CreateContext();
        var entries = await db.LedgerEntries.Where(e => e.TransactionId == result.LedgerTransactionId).ToListAsync(ct);

        entries.Count.ShouldBe(2);
        entries.Single(e => e.LedgerAccountId == customer.Wallet).Amount.ShouldBe(250m);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.ClearingStripeTry).Amount.ShouldBe(-250m);

        // Kart parası IBAN'a çıkmıyor (decisions.md madde 36): card kovasında.
        entries.ShouldAllBe(e => e.FundType == FundType.Card);
        (await BucketAsync(customer.Wallet, FundType.Card, ct)).ShouldBe(250m);
        (await BucketAsync(customer.Wallet, FundType.Cash, ct)).ShouldBe(0m);

        var tx = await db.LedgerTransactions.SingleAsync(t => t.Id == result.LedgerTransactionId, ct);
        tx.Type.ShouldBe(LedgerTransactionType.Topup);
        tx.LedgerAccountId.ShouldBe(customer.Wallet);
        tx.IdempotencyKey.ShouldBe($"{Provider}:{hold.HoldId}");

        // Yüklemeyi hesabın sahibi başlattı.
        tx.ActorType.ShouldBe(ActorType.Customer);
        tx.ActorId.ShouldBe(customer.Account.ToString());

        var fee = await db.ProviderFees.SingleAsync(f => f.TransactionId == result.LedgerTransactionId, ct);
        fee.ExpectedAmount.ShouldBe(decimal.Round(250m * TestProviders.StripeRate, 2, MidpointRounding.AwayFromZero) + TestProviders.StripeFixed);

        var closure = await db.CardTopupHoldClosures.SingleAsync(c => c.HoldId == hold.HoldId, ct);
        closure.Outcome.ShouldBe(CardTopupOutcome.Paid);
        closure.LedgerTransactionId.ShouldBe(result.LedgerTransactionId);
    }

    /// <summary>
    /// Ödendiğinde limit yeniden kontrol edilmiyor ama çifte de sayılmıyor: ödenen para
    /// artık ledger'da, pay kapandı. Çifte sayılsaydı bundan sonra tavan erken dolardı.
    /// </summary>
    [Fact]
    public async Task Odendi_AyniParaIkiKezSayilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 3_000m);
        await PlaceHandler().HandleAsync(hold, ct);
        await CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Paid, 3_000m), ct);

        // 3.000 + 2.500 = 5.500: tam tavan, geçiyor. Çifte sayılsa 8.500 görüp reddederdi.
        await PlaceHandler().HandleAsync(Place(customer.Wallet, 2_500m), ct);

        await Should.ThrowAsync<CardTopupLimitExceededException>(
            () => PlaceHandler().HandleAsync(Place(customer.Wallet, 0.01m), ct));
    }

    [Fact]
    public async Task Odendi_IkiKezGelirse_TekKayit()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 75.50m);
        await PlaceHandler().HandleAsync(hold, ct);
        var message = Closed(hold.HoldId, CardTopupClosedOutcomes.Paid, 75.50m);

        var first = await CloseHandler().HandleAsync(message, ct);
        var second = await CloseHandler().HandleAsync(message, ct);

        second.Replayed.ShouldBeTrue();
        second.LedgerTransactionId.ShouldBe(first.LedgerTransactionId);
        (await BucketAsync(customer.Wallet, FundType.Card, ct)).ShouldBe(75.50m);
    }

    [Fact]
    public async Task Odendi_EsZamanliIkiTeslim_TekKezYazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 10m);
        await PlaceHandler().HandleAsync(hold, ct);
        var message = Closed(hold.HoldId, CardTopupClosedOutcomes.Paid, 10m);

        var results = await Task.WhenAll(
            CloseHandler().HandleAsync(message, ct),
            CloseHandler().HandleAsync(message, ct));

        results.Count(r => r.Replayed).ShouldBe(1);
        (await BucketAsync(customer.Wallet, FundType.Card, ct)).ShouldBe(10m);
    }

    [Fact]
    public async Task Odenmedi_LedgereYazmaz_PayiSerbestBirakir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 4_000m);
        await PlaceHandler().HandleAsync(hold, ct);

        var result = await CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Failed, 4_000m), ct);

        result.Outcome.ShouldBe(CardTopupOutcome.Failed);
        result.LedgerTransactionId.ShouldBeNull();
        (await BucketAsync(customer.Wallet, FundType.Card, ct)).ShouldBe(0m);

        // Pay serbest: limitin tamamı yine kullanılabilir.
        await PlaceHandler().HandleAsync(Place(customer.Wallet, 5_500m), ct);

        // İkinci bildirim zararsız.
        var again = await CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Failed, 4_000m), ct);
        again.Replayed.ShouldBeTrue();
    }

    // ------------------------------------------------------------------
    // Alarm: iki servisin bildiği şey ayrışmış
    // ------------------------------------------------------------------
    [Fact]
    public async Task OdenmediDiyeKapanmisYuklemeninParasiGelirse_Alarm()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 100m);
        await PlaceHandler().HandleAsync(hold, ct);
        await CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Failed, 100m), ct);

        await Should.ThrowAsync<CardTopupRejectedException>(
            () => CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Paid, 100m), ct));

        (await BucketAsync(customer.Wallet, FundType.Card, ct)).ShouldBe(0m);
    }

    [Fact]
    public async Task ParasiYazilmisYuklemeyeOdenmediGelirse_Alarm()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 100m);
        await PlaceHandler().HandleAsync(hold, ct);
        await CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Paid, 100m), ct);

        await Should.ThrowAsync<CardTopupRejectedException>(
            () => CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Failed, 100m), ct));

        (await BucketAsync(customer.Wallet, FundType.Card, ct)).ShouldBe(100m);
    }

    [Fact]
    public async Task PayiOlmayanKimlik_Alarm()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<CardTopupRejectedException>(
            () => CloseHandler().HandleAsync(Closed(Guid.NewGuid(), CardTopupClosedOutcomes.Paid, 10m), ct));
    }

    [Fact]
    public async Task PayliUyusmayanTutar_Alarm_VeCuzdanaYazmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 100m);
        await PlaceHandler().HandleAsync(hold, ct);

        await Should.ThrowAsync<CardTopupRejectedException>(
            () => CloseHandler().HandleAsync(Closed(hold.HoldId, CardTopupClosedOutcomes.Paid, 5_000m), ct));

        (await BucketAsync(customer.Wallet, FundType.Card, ct)).ShouldBe(0m);
    }

    [Fact]
    public async Task BilinmeyenSonuc_Alarm()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var hold = Place(customer.Wallet, 100m);
        await PlaceHandler().HandleAsync(hold, ct);

        await Should.ThrowAsync<CardTopupRejectedException>(
            () => CloseHandler().HandleAsync(Closed(hold.HoldId, "refunded", 100m), ct));
    }
}
