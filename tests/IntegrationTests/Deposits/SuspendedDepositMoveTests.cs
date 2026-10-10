using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Deposits;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Deposits;

/// <summary>
/// Askıdaki havalenin bir hesabın varsayılan cüzdanına aktarılması. Çalışan
/// <c>deposit.resolve</c> izniyle karar veriyor; hesabın seviye limiti yetmiyorsa para
/// askıda kalıyor. Ledger'da yükleme: cüzdan +, askı −, aktör çalışan. Bir havale bir kez
/// çözülüyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SuspendedDepositMoveTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid Account, Guid Wallet, string Number)> AccountAsync(
        AccountType type, CancellationToken ct, bool withWallet = true)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, type, ct, KycLevel.Unverified);
        var wallet = withWallet ? await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct) : Guid.Empty;
        var number = await db.Accounts.Where(a => a.Id == account).Select(a => a.Number).SingleAsync(ct);

        return (account, wallet, number.Value);
    }

    /// <summary>Açıklamasında numara olmayan havale: askıya düşüyor.</summary>
    private async Task<Guid> SuspendedAsync(decimal amount, CancellationToken ct)
    {
        var handler = new ProcessDepositHandler(
            postgres.ContextFactory, new FakeHolderIdentity(), TestKycLimits.Policy,
            new SystemClock(), NullLogger<ProcessDepositHandler>.Instance);

        var result = await handler.HandleAsync(new BankDepositReceived
        {
            Provider = SystemAccounts.BankFake,
            BankReference = $"GLN{Guid.NewGuid():N}"[..19].ToUpperInvariant(),
            Amount = amount,
            Currency = "TRY",
            Description = "kira",
            SenderNationalId = "10000000078",
            ReceivedAt = DateTimeOffset.UtcNow
        }, ct);

        result.HeldFor.ShouldNotBeNull();
        return result.LedgerTransactionId;
    }

    private HttpClient Staff(string subject, params string[] permissions) =>
        _factory.CreateClient().AsStaff(subject, permissions);

    private static HttpClient Resolver(WalletApiFactory factory, string subject) =>
        factory.CreateClient().AsStaff(subject, StaffPermissions.DepositView, StaffPermissions.DepositResolve);

    private static Task<HttpResponseMessage> MoveAsync(
        HttpClient client, Guid deposit, string accountNumber, string? key, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/suspended-deposits/{deposit}/move")
        {
            Content = JsonContent.Create(new { accountNumber })
        };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return client.SendAsync(request, ct);
    }

    private async Task<IReadOnlyList<Guid>> ListedAsync(HttpClient client, CancellationToken ct)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/v1/suspended-deposits?size=100", ct);
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
    }

    [Fact]
    public async Task Calisan_HavaleyiVarsayilanCuzdanaAktarir_AktorCalisan_ListedenCikar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, wallet, number) = await AccountAsync(AccountType.Person, ct);
        var deposit = await SuspendedAsync(20m, ct);
        var subject = $"calisan-{Guid.NewGuid():N}";
        using var staff = Resolver(_factory, subject);

        var response = await MoveAsync(staff, deposit, number, Guid.NewGuid().ToString(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("suspendedDepositId").GetGuid().ShouldBe(deposit);
        body.GetProperty("accountId").GetGuid().ShouldBe(account);
        body.GetProperty("walletId").GetGuid().ShouldBe(wallet);
        body.GetProperty("replayed").GetBoolean().ShouldBeFalse();

        await using var db = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(db, wallet, ct)).ShouldBe(20m);

        var transactionId = body.GetProperty("ledgerTransactionId").GetGuid();
        var tx = await db.LedgerTransactions.AsNoTracking().SingleAsync(t => t.Id == transactionId, ct);
        tx.Type.ShouldBe(LedgerTransactionType.Topup);
        tx.ActorType.ShouldBe(ActorType.Employee);
        tx.ActorId.ShouldBe(subject);

        (await ListedAsync(staff, ct)).ShouldNotContain(deposit);
    }

    /// <summary>
    /// Aynı anahtarla tekrar aynı cevabı dönüyor ve ikinci kez yazmıyor. Başka bir anahtarla
    /// gelen ikinci karar, başka bir hesaba bile olsa, reddediliyor: para bir kez aktarılır.
    /// </summary>
    [Fact]
    public async Task Tekrar_AyniCevap_IkinciKarar422()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet, number) = await AccountAsync(AccountType.Person, ct);
        var (_, _, other) = await AccountAsync(AccountType.Person, ct);
        var deposit = await SuspendedAsync(30m, ct);
        using var staff = Resolver(_factory, $"calisan-{Guid.NewGuid():N}");
        var key = Guid.NewGuid().ToString();

        var first = await MoveAsync(staff, deposit, number, key, ct);
        var replay = await MoveAsync(staff, deposit, number, key, ct);
        var second = await MoveAsync(staff, deposit, other, Guid.NewGuid().ToString(), ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(ct));
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>(ct);
        replayed.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        replayed.GetProperty("ledgerTransactionId").GetGuid()
            .ShouldBe((await first.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("ledgerTransactionId").GetGuid());

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await second.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString()
            .ShouldBe("deposit_already_resolved");

        await using var db = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(db, wallet, ct)).ShouldBe(30m);
    }

    /// <summary>Limiti aşan havale aktarılamıyor, yalnızca iade edilebilir: askıda kalıyor.</summary>
    [Fact]
    public async Task SeviyeLimitiniAsan_422_AskidaKalir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet, number) = await AccountAsync(AccountType.Person, ct);
        var deposit = await SuspendedAsync(6000m, ct);
        using var staff = Resolver(_factory, $"calisan-{Guid.NewGuid():N}");

        var response = await MoveAsync(staff, deposit, number, Guid.NewGuid().ToString(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(ct));
        (await ListedAsync(staff, ct)).ShouldContain(deposit);

        await using var db = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(db, wallet, ct)).ShouldBe(0m);
    }

    /// <summary>
    /// Hedef havalenin cüzdana geçme kuralına uymalı: bireysel hesap, bu para biriminde
    /// varsayılan cüzdan. Kontrol hanesi tutmayan numara 400, olmayan numara 404.
    /// </summary>
    [Fact]
    public async Task UygunOlmayanHedef_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, _, business) = await AccountAsync(AccountType.Business, ct);
        var (_, _, walletless) = await AccountAsync(AccountType.Person, ct, withWallet: false);
        var deposit = await SuspendedAsync(10m, ct);
        using var staff = Resolver(_factory, $"calisan-{Guid.NewGuid():N}");

        (await MoveAsync(staff, deposit, business, Guid.NewGuid().ToString(), ct)).StatusCode
            .ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await MoveAsync(staff, deposit, walletless, Guid.NewGuid().ToString(), ct)).StatusCode
            .ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await MoveAsync(staff, deposit, "1234567890", Guid.NewGuid().ToString(), ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await MoveAsync(staff, deposit, AccountNumber.New().Value, Guid.NewGuid().ToString(), ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await MoveAsync(staff, Guid.NewGuid(), business, Guid.NewGuid().ToString(), ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        (await ListedAsync(staff, ct)).ShouldContain(deposit);
    }

    /// <summary>Para hareketi başlatan her uçta olduğu gibi anahtarsız istek 400.</summary>
    [Fact]
    public async Task AnahtarsizIstek_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, _, number) = await AccountAsync(AccountType.Person, ct);
        var deposit = await SuspendedAsync(10m, ct);
        using var staff = Resolver(_factory, $"calisan-{Guid.NewGuid():N}");

        (await MoveAsync(staff, deposit, number, key: null, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Görmek aktarmaya yetmiyor; müşteri hiç erişemiyor.</summary>
    [Fact]
    public async Task IzinsizCalisan_VeMusteri_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, _, number) = await AccountAsync(AccountType.Person, ct);
        var deposit = await SuspendedAsync(10m, ct);
        using var viewer = Staff($"calisan-{Guid.NewGuid():N}", StaffPermissions.DepositView);
        using var customer = _factory.CreateClient().AsOwnerOf(account);

        (await MoveAsync(viewer, deposit, number, Guid.NewGuid().ToString(), ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await MoveAsync(customer, deposit, number, Guid.NewGuid().ToString(), ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }
}
