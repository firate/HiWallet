using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// Kayıt ve temel doğrulama, bireysel müşterinin iki ön API'sinden: web BFF'i ve mobil
/// ön API. Arkada gerçek onboarding ve gerçek wallet-api. Kayıt uçları kimliksiz;
/// doğrulama uçları web'de oturumun, mobilde istemcinin token'ıyla.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OnboardingEdgeTests(PostgresFixture postgres, OnboardingFixture onboardingDb) : IAsyncLifetime
{
    private WalletApiFactory _walletApi = null!;
    private OnboardingApiFactory _onboarding = null!;
    private PersonalWebBffFactory _web = null!;
    private PersonalMobileApiFactory _mobile = null!;

    public ValueTask InitializeAsync()
    {
        _walletApi = new WalletApiFactory(postgres);
        _onboarding = new OnboardingApiFactory(onboardingDb, new PassthroughHandler(_walletApi.CreateClient()));
        _web = new PersonalWebBffFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            onboarding: new PassthroughHandler(_onboarding.CreateClient()));
        _mobile = new PersonalMobileApiFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            onboarding: new PassthroughHandler(_onboarding.CreateClient()));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _mobile.DisposeAsync();
        await _web.DisposeAsync();
        await _onboarding.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    /// <summary>Kaydı ön API üzerinden baştan sona yürütür; açılan kimliği döner.</summary>
    private async Task<string> RegisterThroughAsync(HttpClient anonymous, string email, CancellationToken ct)
    {
        var started = await anonymous.PostAsJsonAsync("/v1/registrations", new { email }, ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var registrationId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("registrationId").GetGuid();

        var verified = await anonymous.PostAsJsonAsync(
            $"/v1/registrations/{registrationId}/email-verification", new { code = _onboarding.Emails.LastCodeFor(email) }, ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);

        var completed = await anonymous.PostAsJsonAsync(
            $"/v1/registrations/{registrationId}/completion", new { password = "Guclu-Parola-1" }, ct);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _walletApi.Errors));
        (await completed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accountId").GetGuid()
            .ShouldNotBe(Guid.Empty);

        return _onboarding.IdentityProvider.Users[email].Subject;
    }

    [Fact]
    public async Task WebBff_KayitOturumsuz_DogrulamaOturumla()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"web-{Guid.NewGuid():N}@ornek.com";
        using var anonymous = _web.CreateClient().WithCsrfHeader();

        var subject = await RegisterThroughAsync(anonymous, email, ct);

        using var signedIn = _web.CreateClient().SignedInAs(subject).WithCsrfHeader();
        var status = await signedIn.GetFromJsonAsync<JsonElement>("/v1/me/onboarding", ct);
        status.GetProperty("email").GetString().ShouldBe(email);
        status.GetProperty("phoneVerified").GetBoolean().ShouldBeFalse();

        (await anonymous.GetAsync("/v1/me/onboarding", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mobil_KayitKimliksiz_DogrulamaTokenla()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"mobil-{Guid.NewGuid():N}@ornek.com";
        using var anonymous = _mobile.CreateClient();

        var subject = await RegisterThroughAsync(anonymous, email, ct);

        using var customer = _mobile.CreateClient().As(subject);
        var started = await customer.PostAsJsonAsync("/v1/me/phone-verifications", new { phone = "05321234567" }, ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("phone").GetString()
            .ShouldBe("+90 532 *** ** 67");

        (await anonymous.GetAsync("/v1/me/onboarding", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>İç servisin reddi aynen geçiyor: kural adı gövdede.</summary>
    [Fact]
    public async Task YanlisKod_IcServisinReddiAynenDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"red-{Guid.NewGuid():N}@ornek.com";
        using var anonymous = _mobile.CreateClient();
        var started = await anonymous.PostAsJsonAsync("/v1/registrations", new { email }, ct);
        var registrationId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("registrationId").GetGuid();

        var response = await anonymous.PostAsJsonAsync(
            $"/v1/registrations/{registrationId}/email-verification", new { code = "000000" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("wrong_code");
    }
}
