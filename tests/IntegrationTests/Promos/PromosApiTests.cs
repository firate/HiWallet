using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Promos;

/// <summary>
/// Promo'nun HTTP sınırı: işyerinin promo vermesi ve cüzdanın partilerinin listesi
/// (decisions.md madde 37).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PromosApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _shop;
    private Guid _merchant;
    private Guid _customer;
    private Guid _person;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            _shop = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            var customer = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var person = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);

            _merchant = await LedgerSeeder.CreateWalletAsync(db, _shop, "API işyeri", ct);
            _customer = await LedgerSeeder.CreateWalletAsync(db, customer, "API müşteri", ct);
            _person = await LedgerSeeder.CreateWalletAsync(db, person, "API kişi", ct);

            await LedgerSeeder.FundAsync(db, _merchant, 1_000m, ct);
            await LedgerSeeder.FundAsync(db, _person, 1_000m, ct);
        }

        _factory = new WalletApiFactory(postgres);
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static HttpRequestMessage Post(object body, bool omitKey = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/promos") { Content = JsonContent.Create(body) };

        if (!omitKey) request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return request;
    }

    private object Grant(decimal amount, Guid? funder = null, DateTimeOffset? expiresAt = null) => new
    {
        funderWalletId = funder ?? _merchant,
        walletId = _customer,
        amount,
        currency = "TRY",
        expiresAt
    };

    [Fact]
    public async Task Post_AnahtarYok_400Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        using var response = await _client.SendAsync(Post(Grant(10m), omitKey: true), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_GecmisBitisTarihi_400Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        using var response = await _client.SendAsync(
            Post(Grant(10m, expiresAt: DateTimeOffset.UtcNow.AddDays(-1))), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_FonlayanKisiyse_422Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        using var response = await _client.SendAsync(Post(Grant(10m, funder: _person)), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("rule").GetString().ShouldBe("promo_grant_rejected");
    }

    [Fact]
    public async Task Post_201Doner_PartiListedeKalaniylaGorunur()
    {
        var ct = TestContext.Current.CancellationToken;
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        using var created = await _client.SendAsync(Post(Grant(40m, expiresAt: expiresAt)), ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var grantId = body.GetProperty("grantId").GetGuid();
        body.GetProperty("replayed").GetBoolean().ShouldBeFalse();

        var list = await _client.GetFromJsonAsync<JsonElement>($"/v1/wallets/{_customer}/promos?size=100", ct);

        var item = list.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("grantId").GetGuid() == grantId);

        item.GetProperty("amount").GetDecimal().ShouldBe(40m);
        item.GetProperty("remaining").GetDecimal().ShouldBe(40m);
        item.GetProperty("currency").GetString().ShouldBe("TRY");
        item.GetProperty("funder").GetString().ShouldBe("business");
        item.GetProperty("scope").GetString().ShouldBe("selected_businesses");
        item.GetProperty("merchantAccountIds").EnumerateArray().Single().GetGuid().ShouldBe(_shop);
        item.GetProperty("expired").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Liste_CursorIleSayfalanir()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid wallet;
        await using (var db = postgres.CreateContext())
        {
            var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            wallet = await LedgerSeeder.CreateWalletAsync(db, account, "API sayfa", ct);
        }

        for (var i = 0; i < 3; i++)
        {
            using var response = await _client.SendAsync(Post(new
            {
                funderWalletId = _merchant, walletId = wallet, amount = 5m, currency = "TRY"
            }), ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var first = await _client.GetFromJsonAsync<JsonElement>($"/v1/wallets/{wallet}/promos?size=2", ct);
        first.GetProperty("items").GetArrayLength().ShouldBe(2);
        var cursor = first.GetProperty("nextCursor").GetGuid();

        var second = await _client.GetFromJsonAsync<JsonElement>(
            $"/v1/wallets/{wallet}/promos?size=2&after={cursor}", ct);
        second.GetProperty("items").GetArrayLength().ShouldBe(1);
        second.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);

        var ids = first.GetProperty("items").EnumerateArray()
            .Concat(second.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("grantId").GetGuid())
            .ToArray();

        ids.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Liste_CuzdanYoksa_404Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        using var response = await _client.GetAsync($"/v1/wallets/{Guid.NewGuid()}/promos", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_IsyerininNakdiniDuser()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var before = postgres.CreateContext();
        var cash = await before.LedgerBalances
            .Where(b => b.LedgerAccountId == _merchant && b.FundType == FundType.Cash)
            .Select(b => b.Balance)
            .SingleAsync(ct);

        using var response = await _client.SendAsync(Post(Grant(25m)), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var after = postgres.CreateContext();
        (await after.LedgerBalances
                .Where(b => b.LedgerAccountId == _merchant && b.FundType == FundType.Cash)
                .Select(b => b.Balance)
                .SingleAsync(ct))
            .ShouldBe(cash - 25m);
    }
}
