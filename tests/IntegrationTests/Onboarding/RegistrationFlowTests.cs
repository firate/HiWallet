using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.Onboarding;

/// <summary>
/// Kayıt: e-posta, adrese giden kod, parola. Kod doğrulanmadan kullanıcı açılmıyor;
/// kayıt tamamlanınca kimlik sağlayıcıda kullanıcı ve wallet'ta <c>Unknown</c>
/// seviyesinde bireysel hesap var. Uçlar kimliksiz: müşterinin henüz hesabı yok.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RegistrationFlowTests(PostgresFixture postgres, OnboardingFixture onboardingDb) : IAsyncLifetime
{
    private const string Password = "Guclu-Parola-1";

    private WalletApiFactory _walletApi = null!;
    private OnboardingApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _walletApi = new WalletApiFactory(postgres);
        _factory = new OnboardingApiFactory(onboardingDb, new PassthroughHandler(_walletApi.CreateClient()));
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private static string NewEmail() => $"kayit-{Guid.NewGuid():N}@ornek.com";

    private async Task<Guid> StartAsync(string email, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync("/v1/registrations", new { email }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("registrationId").GetGuid();
    }

    private Task<HttpResponseMessage> VerifyAsync(Guid registrationId, string code, CancellationToken ct) =>
        _client.PostAsJsonAsync($"/v1/registrations/{registrationId}/email-verification", new { code }, ct);

    private Task<HttpResponseMessage> CompleteAsync(Guid registrationId, string password, CancellationToken ct) =>
        _client.PostAsJsonAsync($"/v1/registrations/{registrationId}/completion", new { password }, ct);

    private async Task<Guid> VerifiedAsync(string email, CancellationToken ct)
    {
        var registrationId = await StartAsync(email, ct);
        (await VerifyAsync(registrationId, _factory.Emails.LastCodeFor(email), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return registrationId;
    }

    private static async Task<string?> RuleAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).TryGetProperty("rule", out var rule)
            ? rule.GetString()
            : null;

    [Fact]
    public async Task Kayit_KodlaDogrulanir_KullaniciVeHesapAcilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = NewEmail();

        var registrationId = await StartAsync(email, ct);
        var verified = await VerifyAsync(registrationId, _factory.Emails.LastCodeFor(email), ct);
        var completed = await CompleteAsync(registrationId, Password, ct);

        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        completed.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));
        var body = await completed.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("email").GetString().ShouldBe(email);
        var accountId = body.GetProperty("accountId").GetGuid();

        // Kimlik sağlayıcıda e-postası doğrulanmış kullanıcı, parolasıyla.
        var user = _factory.IdentityProvider.Users[email];
        user.Password.ShouldBe(Password);

        // Wallet'ta kullanıcının hesabı, Unknown seviyesinde.
        using var customer = _walletApi.CreateClient().As(user.Subject);
        var account = await customer.GetFromJsonAsync<JsonElement>($"/v1/accounts/{accountId}", ct);
        account.GetProperty("kycLevel").GetString().ShouldBe("Unknown");
    }

    [Fact]
    public async Task YanlisKod_Reddedilir_KullaniciAcilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = NewEmail();
        var registrationId = await StartAsync(email, ct);

        var wrong = await VerifyAsync(registrationId, "000000", ct);
        var completed = await CompleteAsync(registrationId, Password, ct);

        wrong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(wrong, ct)).ShouldBe("wrong_code");
        completed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(completed, ct)).ShouldBe("email_not_verified");
        _factory.IdentityProvider.Users.ContainsKey(email).ShouldBeFalse();
    }

    [Fact]
    public async Task Tamamlama_Tekrarlanabilir_AyniHesap()
    {
        var ct = TestContext.Current.CancellationToken;
        var registrationId = await VerifiedAsync(NewEmail(), ct);

        var first = await CompleteAsync(registrationId, Password, ct);
        var second = await CompleteAsync(registrationId, Password, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accountId").GetGuid()
            .ShouldBe((await first.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accountId").GetGuid());
    }

    /// <summary>
    /// Adresin sahibi olduğu kanıtlandıktan sonra söyleniyor: kod doğrulanmadan
    /// "bu adres kayıtlı" demek başkasının e-postasının müşteri olup olmadığını verirdi.
    /// </summary>
    [Fact]
    public async Task KayitliEposta_KodDogrulandiktanSonra409()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = NewEmail();
        (await CompleteAsync(await VerifiedAsync(email, ct), Password, ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var registrationId = await StartAsync(email, ct);
        (await VerifyAsync(registrationId, _factory.Emails.LastCodeFor(email), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await CompleteAsync(registrationId, Password, ct);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Kimlik sağlayıcıda kullanıcı açılıp kayda bağlanmadan kesilen bir tamamlama:
    /// kullanıcı bu adresle kaydı kimse tamamlamamışken var. Kendi kaydının yarım kalan
    /// kullanıcısı; yeniden kullanılıyor ve parolası yeni parolayla değişiyor.
    /// </summary>
    [Fact]
    public async Task YarimKalanKullanici_KaydaBaglanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = NewEmail();
        var orphan = _factory.IdentityProvider.Seed(email, "Eski-Parola-1");
        var registrationId = await VerifiedAsync(email, ct);

        var completed = await CompleteAsync(registrationId, Password, ct);

        completed.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));
        _factory.IdentityProvider.Users[email].ShouldBe((orphan, Password));
    }

    [Fact]
    public async Task KisaParola_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var registrationId = await VerifiedAsync(NewEmail(), ct);

        var response = await CompleteAsync(registrationId, "kisa", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GecersizEposta_400()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("/v1/registrations", new { email = "adres-degil" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OlmayanKayit_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await VerifyAsync(Guid.NewGuid(), "123456", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
