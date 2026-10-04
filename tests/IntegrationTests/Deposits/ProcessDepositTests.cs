using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Deposits;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Deposits;

/// <summary>
/// Banka hesabımıza gelen havalenin nereye yazıldığı. Para mesaj geldiğinde zaten
/// bankamızda; karar yalnızca cüzdan mı askı mı. Cüzdana yalnızca açıklamadaki numaranın
/// sahibinin kendi hesabından gelen ve seviyenin limitine sığan havale geçiyor. Broker
/// gerekmiyor: mesaj doğrudan handler'a veriliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProcessDepositTests(PostgresFixture postgres)
{
    private const string HolderNationalId = "10000000146";
    private const string OtherNationalId = "10000000078";

    private readonly FakeHolderIdentity _holders = new();

    private ProcessDepositHandler Handler() => new(
        postgres.ContextFactory,
        _holders,
        TestKycLimits.Policy,
        new SystemClock(),
        NullLogger<ProcessDepositHandler>.Instance);

    private static BankDepositReceived Message(
        string? description, decimal amount = 100m, string? senderNationalId = HolderNationalId, string currency = "TRY") => new()
    {
        Provider = SystemAccounts.BankFake,
        BankReference = $"GLN{Guid.NewGuid():N}"[..19].ToUpperInvariant(),
        Amount = amount,
        Currency = currency,
        Description = description,
        SenderNationalId = senderNationalId,
        ReceivedAt = DateTimeOffset.UtcNow
    };

    /// <summary>Bireysel hesap, TRY cüzdanı ve doğrulanmış sahibi; dönen numara 3-3-4 gruplanmış.</summary>
    private async Task<(Guid Account, Guid Wallet, string Number)> CustomerAsync(
        CancellationToken ct, KycLevel level = KycLevel.Unverified, decimal funds = 0m)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, level);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);

        if (funds > 0m)
        {
            await LedgerSeeder.FundAsync(db, wallet, funds, ct);
        }

        _holders.Verified(TestTokens.SubjectOf(account), HolderNationalId);

        return (account, wallet, await GroupedNumberAsync(db, account, ct));
    }

    private static async Task<string> GroupedNumberAsync(WalletDbContext db, Guid account, CancellationToken ct)
    {
        var number = (await db.Accounts.Where(a => a.Id == account).Select(a => a.Number).SingleAsync(ct)).Value;
        return $"{number[..3]} {number[3..6]} {number[6..]}";
    }

    private async Task<List<LedgerEntry>> EntriesAsync(Guid transactionId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await db.LedgerEntries.Where(e => e.TransactionId == transactionId).ToListAsync(ct);
    }

    private async Task<SuspendedDeposit?> SuspendedAsync(Guid transactionId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await db.SuspendedDeposits.SingleOrDefaultAsync(d => d.LedgerTransactionId == transactionId, ct);
    }

    [Fact]
    public async Task SahibininHavalesi_VarsayilanCuzdanaYazilir_NostroEksilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var message = Message($"Hesap no: {customer.Number}", 250m);

        var result = await Handler().HandleAsync(message, ct);

        result.Replayed.ShouldBeFalse();
        result.HeldFor.ShouldBeNull();

        var entries = await EntriesAsync(result.LedgerTransactionId, ct);
        entries.Count.ShouldBe(2);
        entries.Single(e => e.LedgerAccountId == customer.Wallet).Amount.ShouldBe(250m);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.NostroBankTry).Amount.ShouldBe(-250m);
        entries.ShouldAllBe(e => e.FundType == FundType.Cash);

        await using var db = postgres.CreateContext();
        var tx = await db.LedgerTransactions.SingleAsync(t => t.Id == result.LedgerTransactionId, ct);

        tx.Type.ShouldBe(LedgerTransactionType.Topup);
        tx.LedgerAccountId.ShouldBe(customer.Wallet);
        tx.IdempotencyKey.ShouldBe($"{message.Provider}:{message.BankReference}");

        // Havaleyi müşteri başlattı ve gönderenin o olduğunu kimlik numarası doğruladı.
        tx.ActorType.ShouldBe(ActorType.Customer);
        tx.ActorId.ShouldBe(customer.Account.ToString());

        (await LedgerSeeder.BalanceAsync(db, customer.Wallet, ct)).ShouldBe(250m);
    }

    [Fact]
    public async Task IkiCuzdan_VarsayilanOlanaYazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        Guid savings;

        await using (var db = postgres.CreateContext())
        {
            savings = await LedgerSeeder.CreateWalletAsync(db, customer.Account, "Birikim", ct);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE default_wallets SET wallet_id = {savings} WHERE account_id = {customer.Account}", ct);
        }

        var result = await Handler().HandleAsync(Message(customer.Number, 40m), ct);

        (await EntriesAsync(result.LedgerTransactionId, ct))
            .Single(e => e.Amount > 0m).LedgerAccountId.ShouldBe(savings);
    }

    /// <summary>
    /// Aynı havale hem bildirimle hem taramayla gelebilir, broker da iki kez teslim
    /// edebilir: ikincisi orijinal işlemi dönüyor, ledger'a dokunmuyor.
    /// </summary>
    [Fact]
    public async Task AyniHavaleIkiKez_TekKayit()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var message = Message(customer.Number, 75m);

        var first = await Handler().HandleAsync(message, ct);
        var second = await Handler().HandleAsync(message, ct);

        second.Replayed.ShouldBeTrue();
        second.LedgerTransactionId.ShouldBe(first.LedgerTransactionId);

        await using var db = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(db, customer.Wallet, ct)).ShouldBe(75m);
    }

    /// <summary>
    /// Sahibi belirlenemeyen para askıya: banka bakiyesiyle ledger yine tutuyor (nostro −),
    /// borcumuz askı hesabında. Sebep ayrı bir satırda; gönderenin kişisel verisi wallet'ta yok.
    /// </summary>
    [Fact]
    public async Task AciklamadaNumaraYok_AskiyaAlinir()
    {
        var ct = TestContext.Current.CancellationToken;
        var message = Message("kira", 120m);

        var result = await Handler().HandleAsync(message, ct);

        result.HeldFor.ShouldBe(DepositHoldReason.NoAccountNumber);

        var entries = await EntriesAsync(result.LedgerTransactionId, ct);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.SuspenseBankTry).Amount.ShouldBe(120m);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.NostroBankTry).Amount.ShouldBe(-120m);

        await using var db = postgres.CreateContext();
        var tx = await db.LedgerTransactions.SingleAsync(t => t.Id == result.LedgerTransactionId, ct);

        tx.Type.ShouldBe(LedgerTransactionType.SuspendedDeposit);
        tx.LedgerAccountId.ShouldBe(SystemAccounts.SuspenseBankTry);
        tx.ActorType.ShouldBe(ActorType.System);

        var suspended = await SuspendedAsync(result.LedgerTransactionId, ct);
        suspended.ShouldNotBeNull();
        suspended.Reason.ShouldBe(DepositHoldReason.NoAccountNumber);
        suspended.BankReference.ShouldBe(message.BankReference);
        suspended.Amount.ShouldBe(120m);
        suspended.AccountId.ShouldBeNull();
    }

    [Fact]
    public async Task IkiFarkliNumara_AskiyaAlinir()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await CustomerAsync(ct);
        var second = await CustomerAsync(ct);

        var result = await Handler().HandleAsync(Message($"{first.Number} {second.Number}"), ct);

        result.HeldFor.ShouldBe(DepositHoldReason.AmbiguousAccountNumber);
    }

    [Fact]
    public async Task NumaraninHesabiYok_AskiyaAlinir()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Handler().HandleAsync(Message(AccountNumber.New().Value), ct);

        result.HeldFor.ShouldBe(DepositHoldReason.UnknownAccount);
    }

    [Fact]
    public async Task IsyeriHesabi_AskiyaAlinir()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid business;
        string number;

        await using (var db = postgres.CreateContext())
        {
            business = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            await LedgerSeeder.CreateWalletAsync(db, business, "Kasa", ct);
            number = await GroupedNumberAsync(db, business, ct);
        }

        var result = await Handler().HandleAsync(Message(number), ct);

        result.HeldFor.ShouldBe(DepositHoldReason.BusinessAccount);
        (await SuspendedAsync(result.LedgerTransactionId, ct))!.AccountId.ShouldBe(business);
    }

    [Fact]
    public async Task BuParaBirimindeCuzdanYok_AskiyaAlinir()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid account;
        string number;

        await using (var db = postgres.CreateContext())
        {
            account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, KycLevel.Unverified);
            number = await GroupedNumberAsync(db, account, ct);
        }

        _holders.Verified(TestTokens.SubjectOf(account), HolderNationalId);

        var result = await Handler().HandleAsync(Message(number), ct);

        result.HeldFor.ShouldBe(DepositHoldReason.NoWalletInCurrency);
    }

    [Fact]
    public async Task BildirimdeGonderenKimligiYok_AskiyaAlinir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);

        var result = await Handler().HandleAsync(Message(customer.Number, senderNationalId: null), ct);

        result.HeldFor.ShouldBe(DepositHoldReason.UnknownSender);
        (await SuspendedAsync(result.LedgerTransactionId, ct))!.AccountId.ShouldBe(customer.Account);
    }

    /// <summary>Başkasının hesabından gelen para, numara doğru olsa da cüzdana geçmiyor.</summary>
    [Fact]
    public async Task BaskasininHesabindan_AskiyaAlinir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);

        var result = await Handler().HandleAsync(Message(customer.Number, senderNationalId: OtherNationalId), ct);

        result.HeldFor.ShouldBe(DepositHoldReason.SenderNotHolder);

        await using var db = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(db, customer.Wallet, ct)).ShouldBe(0m);
    }

    [Fact]
    public async Task UnknownSeviye_AskiyaAlinir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct, KycLevel.Unknown);

        var result = await Handler().HandleAsync(Message(customer.Number), ct);

        result.HeldFor.ShouldBe(DepositHoldReason.LimitExceeded);
    }

    /// <summary>Bakiye tavanı: daha önce gelmiş ve harcanmamış para da sayılıyor.</summary>
    [Fact]
    public async Task Unverified_BakiyeTavaniniAsan_AskiyaAlinir_SigaGecer()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct, funds: 5_000m);

        var overCap = await Handler().HandleAsync(Message(customer.Number, 600m), ct);
        var upToCap = await Handler().HandleAsync(Message(customer.Number, 500m), ct);

        overCap.HeldFor.ShouldBe(DepositHoldReason.LimitExceeded);
        upToCap.HeldFor.ShouldBeNull();
    }

    /// <summary>
    /// Havale ve gelen transfer ayın toplam girişini paylaşıyor: bu ay 3.000 transferle
    /// gelmişse 2.600'lük havale toplamı 5.600'e çıkarıyor ve askıya düşüyor.
    /// </summary>
    [Fact]
    public async Task Unverified_AyinGelenTransferleriDeSayilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var sender = await CustomerAsync(ct, KycLevel.Contracted, funds: 5_000m);

        var transfers = new CreateTransferHandler(
            postgres.ContextFactory,
            new LimitPolicy(new Dictionary<TransferType, TransferLimit>()),
            TestKycLimits.Policy,
            new CommissionPolicy(new Dictionary<TransferType, CommissionRate>()),
            new SystemClock(),
            NullLogger<CreateTransferHandler>.Instance);

        await transfers.HandleAsync(
            new CreateTransferCommand(sender.Wallet, customer.Wallet, 3_000m, "TRY", TransferType.P2P, Guid.NewGuid().ToString("N")),
            ct);

        var result = await Handler().HandleAsync(Message(customer.Number, 2_600m), ct);

        result.HeldFor.ShouldBe(DepositHoldReason.LimitExceeded);
    }

    /// <summary>
    /// Kimlik sorusu cevapsız kaldıysa karar verilmiyor: hata yukarı çıkıyor, tüketici
    /// mesajı kuyruğa geri koyuyor. Askıya almak ya da cüzdana yazmak tahmin olurdu.
    /// </summary>
    [Fact]
    public async Task KimlikSorusuCevapsiz_HicbirSeyYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerAsync(ct);
        var message = Message(customer.Number);
        _holders.Unreachable = new HttpRequestException("onboarding kapalı");

        await Should.ThrowAsync<HttpRequestException>(() => Handler().HandleAsync(message, ct));

        await using var db = postgres.CreateContext();
        (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == $"{message.Provider}:{message.BankReference}", ct))
            .ShouldBeFalse();
        (await db.ProcessedEvents.AnyAsync(e => e.EventId == message.BankReference, ct)).ShouldBeFalse();
    }

    /// <summary>
    /// Sistem hesabı olmayan para birimi kalıcı hata: askıya bile yazılamıyor, mesaj
    /// dead-letter'a gidip alarm üretmeli.
    /// </summary>
    [Fact]
    public async Task SistemHesabiOlmayanParaBirimi_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<DepositRejectedException>(
            () => Handler().HandleAsync(Message("kira", currency: "EUR"), ct));
    }
}
