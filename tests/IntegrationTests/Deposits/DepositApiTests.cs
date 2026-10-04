using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Deposits;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Deposits;

/// <summary>
/// Havalenin wallet-api'deki iki yüzü: müşteriye "bu IBAN'a, açıklamaya bu numarayla"
/// bilgisi ve panelde askıdaki havalelerin listesi.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DepositApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>wallet-api'nin appsettings'indeki toplama hesabı.</summary>
    private const string CollectionIban = "TR280009900000000000123456";

    private WalletApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid Account, string Number)> AccountAsync(AccountType type, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, type, ct, KycLevel.Unverified);
        await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);
        var number = await db.Accounts.Where(a => a.Id == account).Select(a => a.Number).SingleAsync(ct);

        return (account, number.Value);
    }

    /// <summary>Askıya düşen bir havale: açıklamada bu hesabın numarası, gönderen başkası.</summary>
    private async Task<Guid> SuspendedAsync(string description, decimal amount, CancellationToken ct)
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
            Description = description,
            SenderNationalId = "10000000078",
            ReceivedAt = DateTimeOffset.UtcNow
        }, ct);

        result.HeldFor.ShouldNotBeNull();
        return result.LedgerTransactionId;
    }

    [Fact]
    public async Task Sahibi_YuklemeBilgisiniGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, number) = await AccountAsync(AccountType.Person, ct);
        using var owner = _factory.CreateClient().AsOwnerOf(account);

        var response = await owner.GetAsync($"/v1/accounts/{account}/deposit-instructions", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _factory.Errors));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("iban").GetString().ShouldBe(CollectionIban);
        body.GetProperty("accountHolder").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("reference").GetString().ShouldBe(number);
        body.GetProperty("currency").GetString().ShouldBe("TRY");
    }

    /// <summary>Başkasının hesabı 404; çalışan müşterinin yerine para yüklemiyor, 403.</summary>
    [Fact]
    public async Task BaskasiVeCalisan_YuklemeBilgisiniGoremez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, _) = await AccountAsync(AccountType.Person, ct);
        using var stranger = _factory.CreateClient().As($"yabanci-{Guid.NewGuid():N}");
        using var staff = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.CustomerView);

        (await stranger.GetAsync($"/v1/accounts/{account}/deposit-instructions", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await staff.GetAsync($"/v1/accounts/{account}/deposit-instructions", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>İşyeri hesabına havale askıya düşüyor; ona yükleme bilgisi verilmiyor.</summary>
    [Fact]
    public async Task IsyeriHesabi_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, _) = await AccountAsync(AccountType.Business, ct);
        using var owner = _factory.CreateClient().AsOwnerOf(account);

        var response = await owner.GetAsync($"/v1/accounts/{account}/deposit-instructions", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Askıdaki havaleler, en yeni önce: sebep ve açıklamadaki numaranın hesabı. Gönderenin
    /// kişisel verisi wallet'ta olmadığı için listede de yok.
    /// </summary>
    [Fact]
    public async Task Calisan_AskidakiHavaleleriGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, number) = await AccountAsync(AccountType.Person, ct);
        var older = await SuspendedAsync("kira", 10m, ct);
        var newer = await SuspendedAsync(number, 20m, ct);
        using var staff = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.DepositView);

        var response = await staff.GetAsync("/v1/suspended-deposits?size=100", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _factory.Errors));
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items").EnumerateArray().ToList();
        var ids = items.Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.IndexOf(newer).ShouldBeLessThan(ids.IndexOf(older));

        var matched = items.Single(i => i.GetProperty("id").GetGuid() == newer);
        matched.GetProperty("reason").GetString().ShouldBe("sender_not_holder");
        matched.GetProperty("amount").GetDecimal().ShouldBe(20m);
        matched.GetProperty("accountId").GetGuid().ShouldBe(account);
        matched.GetProperty("accountNumber").GetString().ShouldBe(number);
        matched.TryGetProperty("senderNationalId", out _).ShouldBeFalse();

        var unmatched = items.Single(i => i.GetProperty("id").GetGuid() == older);
        unmatched.GetProperty("reason").GetString().ShouldBe("no_account_number");
        unmatched.GetProperty("accountId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AskidakiHavaleler_SayfaSayfa()
    {
        var ct = TestContext.Current.CancellationToken;
        await SuspendedAsync("kira", 1m, ct);
        await SuspendedAsync("kira", 2m, ct);
        using var staff = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.DepositView);

        var first = await staff.GetFromJsonAsync<JsonElement>("/v1/suspended-deposits?size=1", ct);
        var cursor = first.GetProperty("nextCursor").GetGuid();
        var second = await staff.GetFromJsonAsync<JsonElement>($"/v1/suspended-deposits?size=1&after={cursor}", ct);

        first.GetProperty("items").GetArrayLength().ShouldBe(1);
        second.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldNotBe(first.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    /// <summary>İzinsiz çalışan ve müşteri göremiyor.</summary>
    [Fact]
    public async Task IzinsizCalisanVeMusteri_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var unauthorized = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.CustomerView);
        using var customer = _factory.CreateClient().As($"musteri-{Guid.NewGuid():N}");

        (await unauthorized.GetAsync("/v1/suspended-deposits", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/v1/suspended-deposits", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
