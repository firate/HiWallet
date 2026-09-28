using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.Accounts;

/// <summary>
/// Bireysel hesabı onboarding açıyor: kayıt tamamlanınca kimliğin hesabı ve ilk TRY
/// cüzdanı <c>Unknown</c> seviyesinde. Kimlik başına tek bireysel hesap; açılış tekrar
/// edilebilir. Müşteri bireysel hesabı kendisi açamıyor, açabilseydi kayıt ve doğrulama
/// adımlarını atlardı.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PersonAccountTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
    private HttpClient _onboarding = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        _onboarding = _factory.CreateClient().AsOnboarding();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _onboarding.Dispose();
        await _factory.DisposeAsync();
    }

    private static string NewHolder() => $"kayit-{Guid.NewGuid():N}";

    private async Task<(HttpStatusCode Status, JsonElement Body)> OpenAsync(string holder, CancellationToken ct)
    {
        var response = await _onboarding.PostAsJsonAsync("/v1/person-accounts", new { holder }, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (response.StatusCode, body);
    }

    private async Task<JsonElement> GetAsHolderAsync(string holder, Guid accountId, CancellationToken ct)
    {
        using var client = _factory.CreateClient().As(holder);
        return await client.GetFromJsonAsync<JsonElement>($"/v1/accounts/{accountId}", ct);
    }

    [Fact]
    public async Task Onboarding_HesabiUnknownSeviyedeVeTryCuzdaniylaAcar()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = NewHolder();

        var (status, body) = await OpenAsync(holder, ct);

        status.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        var accountId = body.GetProperty("accountId").GetGuid();
        body.GetProperty("kycLevel").GetString().ShouldBe("Unknown");
        body.GetProperty("walletId").GetGuid().ShouldNotBe(Guid.Empty);

        // Hesabı açan onboarding ama hesabın kullanıcısı kayıt olan kimlik.
        var account = await GetAsHolderAsync(holder, accountId, ct);
        account.GetProperty("kycLevel").GetString().ShouldBe("Unknown");
        var wallets = account.GetProperty("wallets").EnumerateArray().ToList();
        wallets.Count.ShouldBe(1);
        wallets[0].GetProperty("currency").GetString().ShouldBe("TRY");
    }

    [Fact]
    public async Task AyniKimlikTekrar_AyniHesabiDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = NewHolder();

        var first = await OpenAsync(holder, ct);
        var second = await OpenAsync(holder, ct);

        second.Status.ShouldBe(HttpStatusCode.OK);
        second.Body.GetProperty("accountId").GetGuid().ShouldBe(first.Body.GetProperty("accountId").GetGuid());
        second.Body.GetProperty("walletId").GetGuid().ShouldBe(first.Body.GetProperty("walletId").GetGuid());

        var account = await GetAsHolderAsync(holder, first.Body.GetProperty("accountId").GetGuid(), ct);
        account.GetProperty("wallets").GetArrayLength().ShouldBe(1);
    }

    /// <summary>
    /// Aynı anda gelen açılışlar tek hesap üretiyor: tekillik veritabanında, "önce bak
    /// sonra yaz" iki isteği birlikte geçirirdi.
    /// </summary>
    [Fact]
    public async Task EsZamanliAcilis_TekHesap()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = NewHolder();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => OpenAsync(holder, ct)));

        results.Select(r => r.Body.GetProperty("accountId").GetGuid()).Distinct().Count().ShouldBe(1);
        results.Count(r => r.Status == HttpStatusCode.Created).ShouldBe(1);
        results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(4);
    }

    [Fact]
    public async Task Musteri_BireyselHesapAcamaz()
    {
        var ct = TestContext.Current.CancellationToken;
        using var customer = _factory.CreateClient().As(NewHolder());

        var viaOnboardingEndpoint = await customer.PostAsJsonAsync(
            "/v1/person-accounts", new { holder = NewHolder() }, ct);
        var viaAccounts = await customer.PostAsJsonAsync("/v1/accounts", new { type = "Person" }, ct);

        viaOnboardingEndpoint.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        viaAccounts.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Onboarding_SeviyeyiDegistirir()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = NewHolder();
        var accountId = (await OpenAsync(holder, ct)).Body.GetProperty("accountId").GetGuid();

        var response = await _onboarding.PutAsJsonAsync(
            $"/v1/accounts/{accountId}/kyc-level", new { level = "Unverified" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _factory.Errors));
        (await GetAsHolderAsync(holder, accountId, ct)).GetProperty("kycLevel").GetString().ShouldBe("Unverified");
    }

    [Fact]
    public async Task Musteri_SeviyesiniDegistiremez_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = NewHolder();
        var accountId = (await OpenAsync(holder, ct)).Body.GetProperty("accountId").GetGuid();
        using var customer = _factory.CreateClient().As(holder);

        var response = await customer.PutAsJsonAsync(
            $"/v1/accounts/{accountId}/kyc-level", new { level = "Contracted" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsHolderAsync(holder, accountId, ct)).GetProperty("kycLevel").GetString().ShouldBe("Unknown");
    }

    [Fact]
    public async Task IsyerininSeviyesiYok_422()
    {
        var ct = TestContext.Current.CancellationToken;
        using var merchant = _factory.CreateClient().As(NewHolder());
        var opened = await merchant.PostAsJsonAsync("/v1/accounts", new { type = "Business" }, ct);
        var accountId = (await opened.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accountId").GetGuid();

        var response = await _onboarding.PutAsJsonAsync(
            $"/v1/accounts/{accountId}/kyc-level", new { level = "Unverified" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task OlmayanHesap_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _onboarding.PutAsJsonAsync(
            $"/v1/accounts/{Guid.NewGuid()}/kyc-level", new { level = "Unverified" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
