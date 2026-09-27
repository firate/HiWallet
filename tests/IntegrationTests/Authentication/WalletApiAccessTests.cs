using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.IdentityModel.Tokens;

namespace HiWallet.IntegrationTests.Authentication;

/// <summary>
/// wallet-api kimliği kendisi doğruluyor ve sahipliği kendisi kontrol ediyor: ön API
/// token'ı aynen iletiyor, ama ön API'ye körü körüne güvenilmiyor.
///
/// Sahibi olunmayan kaynak <c>404</c>, <c>403</c> DEĞİL: başkasının cüzdanının var
/// olduğu bilgisi de dışarı verilmiyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WalletApiAccessTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(Guid AccountId, Guid WalletId)> FundedWalletAsync(decimal amount, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var walletId = await LedgerSeeder.CreateWalletAsync(db, accountId, "Ana", ct);

        if (amount > 0)
        {
            await LedgerSeeder.FundAsync(db, walletId, amount, ct);
        }

        return (accountId, walletId);
    }

    private static string NewSubject() => $"test-{Guid.NewGuid():N}";

    [Fact]
    public async Task TokenYok_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, walletId) = await FundedWalletAsync(0m, ct);

        var response = await _client.GetAsync($"/v1/wallets/{walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BaskaAnahtarlaImzalanmisToken_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, walletId) = await FundedWalletAsync(0m, ct);

        var forged = TestTokens.For(TestTokens.SubjectOf(accountId), signingKey: new RsaSecurityKey(RSA.Create(2048)));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await _client.GetAsync($"/v1/wallets/{walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BaskaAudienceIcinToken_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, walletId) = await FundedWalletAsync(0m, ct);

        var token = TestTokens.For(TestTokens.SubjectOf(accountId), audiences: ["baska-bir-api"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync($"/v1/wallets/{walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task KendiCuzdani_200()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, walletId) = await FundedWalletAsync(40m, ct);

        var response = await _client.AsOwnerOf(accountId).GetAsync($"/v1/wallets/{walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _factory.Errors));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/movements")]
    [InlineData("/promos")]
    public async Task BaskasininCuzdani_404(string suffix)
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, walletId) = await FundedWalletAsync(40m, ct);

        var response = await _client.As(NewSubject()).GetAsync($"/v1/wallets/{walletId}{suffix}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BaskasininHesabi_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, _) = await FundedWalletAsync(0m, ct);

        var response = await _client.As(NewSubject()).GetAsync($"/v1/accounts/{accountId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BaskasininHesabinaCuzdanAcilamaz_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, _) = await FundedWalletAsync(0m, ct);

        var response = await _client.As(NewSubject())
            .PostAsJsonAsync($"/v1/accounts/{accountId}/wallets", new { name = "Gizli", currency = "TRY" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Hesabı açan kimlik hesabın kullanıcısı oluyor. Hesaplarını listeleyen uç
    /// yalnızca onun hesaplarını dönüyor.
    /// </summary>
    [Fact]
    public async Task HesapAcma_KimligiHesabaBaglar()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = NewSubject();

        var opened = await _client.As(owner).PostAsJsonAsync("/v1/accounts", new { type = "Person" }, ct);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        var accountId = (await opened.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accountId").GetGuid();

        var mine = await _client.As(owner).GetFromJsonAsync<JsonElement>("/v1/accounts", ct);
        mine.GetProperty("items").EnumerateArray()
            .Select(a => a.GetProperty("accountId").GetGuid())
            .ShouldBe([accountId]);

        (await _client.As(owner).GetAsync($"/v1/accounts/{accountId}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.As(NewSubject()).GetAsync($"/v1/accounts/{accountId}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var others = await _client.As(NewSubject()).GetFromJsonAsync<JsonElement>("/v1/accounts", ct);
        others.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task BaskasininCuzdanindanTransfer_404VeLedgerDegismez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, from) = await FundedWalletAsync(100m, ct);
        var (_, to) = await FundedWalletAsync(0m, ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(new { fromWalletId = from, toWalletId = to, amount = 30m, currency = "TRY", type = "P2P" })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.As(NewSubject()).SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var db = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(db, from, ct)).ShouldBe(100m);
    }

    [Fact]
    public async Task BaskasininIsyeriCuzdanindanPromo_404()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid funder;
        await using (var db = postgres.CreateContext())
        {
            var merchant = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            funder = await LedgerSeeder.CreateWalletAsync(db, merchant, "Kasa", ct);
            await LedgerSeeder.FundAsync(db, funder, 500m, ct);
        }

        var (_, customerWallet) = await FundedWalletAsync(0m, ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/promos")
        {
            Content = JsonContent.Create(new { funderWalletId = funder, walletId = customerWallet, amount = 50m, currency = "TRY" })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.As(NewSubject()).SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
