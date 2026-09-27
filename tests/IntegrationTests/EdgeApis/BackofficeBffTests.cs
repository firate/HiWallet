using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// backoffice-bff: çalışanın paneli, arkasında gerçek wallet-api ve orchestrator ile.
/// Oturum çalışanların realm'inden; iç servise giden token çalışanın ve rollerini
/// taşıyor. Görüntüleme yetkisini iç servis de kontrol ediyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackofficeBffTests(PostgresFixture postgres, OrchestratorFixture orchestratorDb)
    : IAsyncLifetime
{
    private WalletApiFactory _walletApi = null!;
    private WithdrawalOrchestratorApiFactory _orchestrator = null!;
    private BackofficeBffFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _customer;
    private Guid _wallet;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            _customer = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            _wallet = await LedgerSeeder.CreateWalletAsync(db, _customer, "Ana", ct);
            await LedgerSeeder.FundAsync(db, _wallet, 250m, ct);
        }

        _walletApi = new WalletApiFactory(postgres);
        _orchestrator = new WithdrawalOrchestratorApiFactory(orchestratorDb);
        _factory = new BackofficeBffFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            withdrawalOrchestrator: new PassthroughHandler(_orchestrator.CreateClient()));
        _client = _factory.CreateClient().WithCsrfHeader();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _orchestrator.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private static string NewStaff() => $"calisan-{Guid.NewGuid():N}";

    [Fact]
    public async Task Destek_MusterininKaydiniGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.SignedInAs(NewStaff(), StaffRoles.Support);

        var account = await _client.GetAsync($"/v1/accounts/{_customer}", ct);
        account.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _walletApi.Errors));
        (await account.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("wallets")[0].GetProperty("walletId").GetGuid().ShouldBe(_wallet);

        var wallet = await _client.GetFromJsonAsync<JsonElement>($"/v1/wallets/{_wallet}", ct);
        wallet.GetProperty("balance").GetDecimal().ShouldBe(250m);

        (await _client.GetAsync($"/v1/wallets/{_wallet}/movements", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetAsync($"/v1/wallets/{_wallet}/promos", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Destek_MusterininCekiminiGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = Guid.NewGuid();

        using var customer = _orchestrator.CreateClient().As(TestTokens.SubjectOf(accountId));
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                walletId = Guid.NewGuid(),
                amount = 100m,
                currency = "TRY",
                destinationIban = "TR330006100519786457841326"
            }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        var started = await customer.SendAsync(request, ct);
        var withdrawalId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("withdrawalId").GetGuid();

        var response = await _client.SignedInAs(NewStaff(), StaffRoles.Support)
            .GetAsync($"/v1/withdrawals/{withdrawalId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Rolü olmayan çalışan panelde hiçbir şey görmüyor: BFF iç servise gitmeden
    /// reddediyor. Kullanıcı bilgisi de kapalı; arayüz yetkisizliği buradan anlıyor.
    /// </summary>
    [Fact]
    public async Task RolsuzCalisan_403()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.SignedInAs(NewStaff());

        (await _client.GetAsync($"/v1/wallets/{_wallet}", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _client.GetAsync("/bff/user", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Kullanici_RolleriyleDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var staff = NewStaff();
        _client.SignedInAs(staff, StaffRoles.Support, StaffRoles.Operations);

        var user = await _client.GetFromJsonAsync<JsonElement>("/bff/user", ct);

        user.GetProperty("subject").GetString().ShouldBe(staff);
        user.GetProperty("roles").EnumerateArray().Select(role => role.GetString())
            .ShouldBe([StaffRoles.Support, StaffRoles.Operations], ignoreOrder: true);
    }

    /// <summary>Pazarlama işyerinin promo kabulünü panelden işaretliyor.</summary>
    [Fact]
    public async Task Pazarlama_PromoKabulunuIsaretler()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid merchant;
        await using (var db = postgres.CreateContext())
        {
            merchant = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
        }

        _client.SignedInAs(NewStaff(), StaffRoles.Marketing);

        var response = await _client.PutAsJsonAsync($"/v1/accounts/{merchant}/accepts-promo", new { acceptsPromo = true }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, string.Join("\n", _walletApi.Errors));
        (await _client.GetFromJsonAsync<JsonElement>($"/v1/accounts/{merchant}", ct))
            .GetProperty("acceptsPromo").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// Personel promo'su panelden: anahtar BFF'ten iç servise aynen gidiyor ve aynı
    /// anahtarla tekrar yeni parti açmıyor.
    /// </summary>
    [Fact]
    public async Task Pazarlama_PersonelPromoVerir()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.SignedInAs(NewStaff(), StaffRoles.Marketing);
        var key = Guid.NewGuid().ToString();

        HttpRequestMessage Grant() => new(HttpMethod.Post, $"/v1/wallets/{_wallet}/promos")
        {
            Content = JsonContent.Create(new { amount = 15m, currency = "TRY", scope = "all_businesses" }),
            Headers = { { "Idempotency-Key", key } }
        };

        var first = await _client.SendAsync(Grant(), ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));
        first.Headers.Location!.AbsolutePath.ShouldBe($"/v1/wallets/{_wallet}/promos");

        var second = await _client.SendAsync(Grant(), ct);
        (await second.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("replayed").GetBoolean().ShouldBeTrue();
    }

    /// <summary>Rolün yetkisini wallet-api kontrol ediyor; BFF reddi aynen aktarıyor.</summary>
    [Fact]
    public async Task Destek_PersonelPromoVeremez_403()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.SignedInAs(NewStaff(), StaffRoles.Support);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/wallets/{_wallet}/promos")
        {
            Content = JsonContent.Create(new { amount = 15m, currency = "TRY", scope = "all_businesses" }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        (await _client.SendAsync(request, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pazarlama_KampanyaAcarVeBitirir()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.SignedInAs(NewStaff(), StaffRoles.Marketing);

        var created = await _client.PostAsJsonAsync("/v1/promo-campaigns", new
        {
            name = $"Panel {Guid.NewGuid():N}",
            rule = "daily_payment_total",
            thresholdAmount = 500m,
            rewardType = "fixed",
            rewardAmount = 10m,
            currency = "TRY",
            grantScope = "all_businesses",
            budget = 1000m,
            dailyCapPerAccount = 10m,
            totalCapPerAccount = 50m,
            startsAt = DateTimeOffset.UtcNow.AddDays(1)
        }, ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("campaignId").GetGuid();
        created.Headers.Location!.AbsolutePath.ShouldBe($"/v1/promo-campaigns/{id}");

        var ended = await _client.PostAsync($"/v1/promo-campaigns/{id}/end", null, ct);
        ended.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ended.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("endsAt").ValueKind
            .ShouldNotBe(JsonValueKind.Null);
    }

    /// <summary>
    /// Operasyon inceleme kuyruğunu panelde görüyor ve çekimi serbest bırakıyor; karar
    /// orchestrator'da, çalışanın token'ıyla.
    /// </summary>
    [Fact]
    public async Task Operasyon_IncelemedekiCekimiSerbestBirakir()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = Guid.NewGuid();

        using var customer = _orchestrator.CreateClient().As(TestTokens.SubjectOf(accountId));
        var started = await customer.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                walletId = Guid.NewGuid(),
                amount = 15_000m,
                currency = "TRY",
                destinationIban = "TR330006100519786457841326"
            }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        }, ct);
        var withdrawalId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("withdrawalId").GetGuid();

        await using (var scope = _orchestrator.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdvanceSagaHandler>().HandleAsync(
                new WithdrawalDebited { SagaId = withdrawalId, LedgerTransactionId = Guid.NewGuid(), TotalDebited = 15_005m }, ct);
        }

        _client.SignedInAs(NewStaff(), StaffRoles.Operations);

        var queue = await _client.GetFromJsonAsync<JsonElement>("/v1/withdrawals?state=under_review&size=100", ct);
        queue.GetProperty("items").EnumerateArray().ShouldContain(w => w.GetProperty("withdrawalId").GetGuid() == withdrawalId);

        var released = await _client.PostAsync($"/v1/withdrawals/{withdrawalId}/release", null, ct);

        released.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await released.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString()
            .ShouldBe("bank_transfer_pending");
    }
}

/// <summary>Backoffice oturumu: giriş çalışanların realm'ine gidiyor.</summary>
public sealed class BackofficeBffSessionTests
{
    [Fact]
    public async Task Giris_CalisanlarinRealmineYonlendiriyor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new BackofficeBffFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/bff/login", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull();
        location.GetLeftPart(UriPartial.Path).ShouldBe($"{TestTokens.StaffIssuer}/protocol/openid-connect/auth");
        HttpUtility.ParseQueryString(location.Query)["client_id"].ShouldBe("backoffice");
    }

    [Fact]
    public async Task OturumYok_401()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new BackofficeBffFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .WithCsrfHeader();

        var response = await client.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
