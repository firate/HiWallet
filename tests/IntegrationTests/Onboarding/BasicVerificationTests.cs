using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.Onboarding;

/// <summary>
/// Temel doğrulama, giriş yaptıktan sonra: telefon SMS koduyla, kimlik bilgileri nüfus
/// kaydıyla, sonra sözleşme ve aydınlatma metninin onayı. Üçü tamamlanınca hesap
/// <c>Unverified</c>'a geçiyor. Kimlik müşterinin kendi token'ından.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BasicVerificationTests(PostgresFixture postgres, OnboardingFixture onboardingDb) : IAsyncLifetime
{
    private const string Phone = "05321234567";

    private WalletApiFactory _walletApi = null!;
    private OnboardingApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _walletApi = new WalletApiFactory(postgres);
        _factory = new OnboardingApiFactory(onboardingDb, new PassthroughHandler(_walletApi.CreateClient()));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    /// <summary>Kaydı tamamlanmış bir müşteri: kimliği ve hesabı.</summary>
    private async Task<(string Subject, Guid AccountId)> RegisteredAsync(CancellationToken ct)
    {
        using var anonymous = _factory.CreateClient();
        var email = $"dogrulama-{Guid.NewGuid():N}@ornek.com";

        var started = await anonymous.PostAsJsonAsync("/v1/registrations", new { email }, ct);
        var registrationId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("registrationId").GetGuid();
        await anonymous.PostAsJsonAsync(
            $"/v1/registrations/{registrationId}/email-verification", new { code = _factory.Emails.LastCodeFor(email) }, ct);
        var completed = await anonymous.PostAsJsonAsync(
            $"/v1/registrations/{registrationId}/completion", new { password = "Guclu-Parola-1" }, ct);
        completed.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));

        var accountId = (await completed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accountId").GetGuid();
        return (_factory.IdentityProvider.Users[email].Subject, accountId);
    }

    private HttpClient Customer(string subject) => _factory.CreateClient().As(subject);

    /// <summary>Müşterinin numarasını doğrular; numara verilmezse yenisi. Doğrulanan numarayı döner.</summary>
    private async Task<string> VerifyPhoneAsync(HttpClient customer, CancellationToken ct, string? phone = null)
    {
        phone ??= PhoneNumbers.New();

        var started = await customer.PostAsJsonAsync("/v1/me/phone-verifications", new { phone }, ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var verificationId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("verificationId").GetGuid();

        var confirmed = await customer.PostAsJsonAsync(
            $"/v1/me/phone-verifications/{verificationId}/confirmation",
            new { code = _factory.Sms.LastCodeFor(PhoneNumbers.E164(phone)) }, ct);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);

        return phone;
    }

    private static Task<HttpResponseMessage> PutIdentityAsync(HttpClient customer, string nationalId, CancellationToken ct) =>
        customer.PutAsJsonAsync("/v1/me/identity", new
        {
            firstName = "Ayşe",
            lastName = "Yılmaz",
            nationalId,
            birthDate = "1990-05-17"
        }, ct);

    private static async Task<HttpResponseMessage> AcceptAsync(HttpClient customer, CancellationToken ct)
    {
        var status = await customer.GetFromJsonAsync<JsonElement>("/v1/me/onboarding", ct);
        var documents = status.GetProperty("documents");

        return await customer.PostAsJsonAsync("/v1/me/basic-verification", new
        {
            termsVersion = documents.GetProperty("termsVersion").GetString(),
            privacyNoticeVersion = documents.GetProperty("privacyNoticeVersion").GetString()
        }, ct);
    }

    [Fact]
    public async Task TelefonKimlikVeOnay_HesapUnverifiedOlur()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, accountId) = await RegisteredAsync(ct);
        using var customer = Customer(subject);

        var phone = await VerifyPhoneAsync(customer, ct);
        var identity = await PutIdentityAsync(customer, NationalIds.New(), ct);
        var accepted = await AcceptAsync(customer, ct);

        identity.StatusCode.ShouldBe(HttpStatusCode.OK);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _walletApi.Errors));
        (await accepted.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("kycLevel").GetString()
            .ShouldBe("Unverified");

        using var wallet = _walletApi.CreateClient().As(subject);
        (await wallet.GetFromJsonAsync<JsonElement>($"/v1/accounts/{accountId}", ct))
            .GetProperty("kycLevel").GetString().ShouldBe("Unverified");

        var status = await customer.GetFromJsonAsync<JsonElement>("/v1/me/onboarding", ct);
        status.GetProperty("phoneVerified").GetBoolean().ShouldBeTrue();
        status.GetProperty("phone").GetString().ShouldBe($"+90 {phone[1..4]} *** ** {phone[^2..]}");
        status.GetProperty("identityVerified").GetBoolean().ShouldBeTrue();
        status.GetProperty("basicVerificationCompleted").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task NufusKaydiEslesmez_422_HesapDegismez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, _) = await RegisteredAsync(ct);
        using var customer = Customer(subject);
        var nationalId = NationalIds.New();
        _factory.Registry.Mismatch(nationalId);

        await VerifyPhoneAsync(customer, ct);
        var identity = await PutIdentityAsync(customer, nationalId, ct);
        var accepted = await AcceptAsync(customer, ct);

        identity.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        accepted.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task TelefonDogrulanmadan_TemelDogrulamaTamamlanmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, accountId) = await RegisteredAsync(ct);
        using var customer = Customer(subject);

        (await PutIdentityAsync(customer, NationalIds.New(), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var accepted = await AcceptAsync(customer, ct);

        accepted.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var wallet = _walletApi.CreateClient().As(subject);
        (await wallet.GetFromJsonAsync<JsonElement>($"/v1/accounts/{accountId}", ct))
            .GetProperty("kycLevel").GetString().ShouldBe("Unknown");
    }

    /// <summary>Bir kimlik numarası tek müşteriye ait; ikinci hesap aynı kişiyle doğrulanmıyor.</summary>
    [Fact]
    public async Task KimlikNumarasiBaskaMusteride_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var nationalId = NationalIds.New();
        var (first, _) = await RegisteredAsync(ct);
        var (second, _) = await RegisteredAsync(ct);
        using var firstCustomer = Customer(first);
        using var secondCustomer = Customer(second);

        (await PutIdentityAsync(firstCustomer, nationalId, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var duplicate = await PutIdentityAsync(secondCustomer, nationalId, ct);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GecersizKimlikNumarasi_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, _) = await RegisteredAsync(ct);
        using var customer = Customer(subject);

        var response = await PutIdentityAsync(customer, "12345678951", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Onaylanan sürüm gösterilen metnin sürümü olmalı; eski sürümün onayı geçmiyor.</summary>
    [Fact]
    public async Task EskiSozlesmeSurumu_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, _) = await RegisteredAsync(ct);
        using var customer = Customer(subject);
        await VerifyPhoneAsync(customer, ct);
        await PutIdentityAsync(customer, NationalIds.New(), ct);

        var response = await customer.PostAsJsonAsync(
            "/v1/me/basic-verification", new { termsVersion = "2020-01", privacyNoticeVersion = "2020-01" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Başkasının telefon doğrulaması görünmüyor: 404, 403 değil.</summary>
    [Fact]
    public async Task BaskasininTelefonDogrulamasi_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await RegisteredAsync(ct);
        using var ownerClient = Customer(owner);
        var started = await ownerClient.PostAsJsonAsync("/v1/me/phone-verifications", new { phone = Phone }, ct);
        var verificationId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("verificationId").GetGuid();

        using var stranger = Customer($"test-{Guid.NewGuid():N}");
        var response = await stranger.PostAsJsonAsync(
            $"/v1/me/phone-verifications/{verificationId}/confirmation",
            new { code = _factory.Sms.LastCodeFor("+905321234567") }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Kimliksiz_401()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/v1/me/onboarding", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
