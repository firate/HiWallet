using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;

namespace HiWallet.IntegrationTests.Onboarding;

/// <summary>
/// Çalışanın müşteriyi tanıması: hesabın sahibinin kişisel bilgileri ve hesabı e-posta,
/// telefon ya da kimlik numarasıyla bulmak. İkisi de <c>customer.view</c> izniyle; kimlik
/// numarası ve telefon maskeli. Arama gövdede: kimlik numarası adrese, yani erişim
/// log'larına düşmüyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomerLookupTests(PostgresFixture postgres, OnboardingFixture onboardingDb) : IAsyncLifetime
{
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

    private HttpClient Staff(params string[] permissions) =>
        _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", permissions);

    private static string MaskedPhone(string phone) => $"+90 {phone[1..4]} *** ** {phone[^2..]}";

    private static string MaskedNationalId(string id) => $"{id[..2]}*******{id[^2..]}";

    private static async Task<JsonElement> SearchAsync(HttpClient staff, object criterion, CancellationToken ct)
    {
        var response = await staff.PostAsJsonAsync("/v1/customer-searches", criterion, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items");
    }

    [Fact]
    public async Task Calisan_HesabinSahibiniGoruyor_KimlikNumarasiVeTelefonMaskeli()
    {
        var ct = TestContext.Current.CancellationToken;
        var nationalId = NationalIds.New();
        var (_, accountId, email, phone) = await OnboardingFlows.VerifiedAsync(_factory, ct, nationalId: nationalId);
        using var staff = Staff(StaffPermissions.CustomerView);

        var response = await staff.GetAsync($"/v1/customers/by-account/{accountId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync(ct);
        raw.ShouldNotContain(nationalId);
        raw.ShouldNotContain(PhoneNumbers.E164(phone)[3..]);

        var customer = JsonDocument.Parse(raw).RootElement;
        customer.GetProperty("accountId").GetGuid().ShouldBe(accountId);
        customer.GetProperty("email").GetString().ShouldBe(email);
        customer.GetProperty("firstName").GetString().ShouldBe("Ayşe");
        customer.GetProperty("lastName").GetString().ShouldBe("Yılmaz");
        customer.GetProperty("nationalId").GetString().ShouldBe(MaskedNationalId(nationalId));
        customer.GetProperty("birthDate").GetString().ShouldBe("1990-05-17");
        customer.GetProperty("phone").GetString().ShouldBe(MaskedPhone(phone));
        customer.GetProperty("basicVerifiedAt").ValueKind.ShouldBe(JsonValueKind.String);
        customer.GetProperty("consents").EnumerateArray()
            .Select(c => c.GetProperty("document").GetString())
            .ShouldBe(["Terms", "PrivacyNotice"], ignoreOrder: true);
        customer.GetProperty("phoneChanges").GetArrayLength().ShouldBe(0);
    }

    /// <summary>Kaydını bitirip doğrulamaya başlamamış müşteri: yalnızca e-posta var.</summary>
    [Fact]
    public async Task DogrulamayaBaslamamis_YalnizcaEposta()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, accountId, email) = await OnboardingFlows.RegisteredAsync(_factory, ct);
        using var staff = Staff(StaffPermissions.CustomerView);

        var customer = await staff.GetFromJsonAsync<JsonElement>($"/v1/customers/by-account/{accountId}", ct);

        customer.GetProperty("email").GetString().ShouldBe(email);
        customer.GetProperty("firstName").ValueKind.ShouldBe(JsonValueKind.Null);
        customer.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
        customer.GetProperty("basicVerifiedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        customer.GetProperty("consents").GetArrayLength().ShouldBe(0);
    }

    /// <summary>Numara ne zaman değişti: hesabı ele geçirme şüphesinde ilk bakılan yer.</summary>
    [Fact]
    public async Task TelefonDegisiklikleri_YenidenEskiye()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, accountId, _, oldPhone) = await OnboardingFlows.VerifiedAsync(_factory, ct);
        var newPhone = PhoneNumbers.New();
        using var customer = _factory.CreateClient().As(subject);

        var started = await customer.PostAsJsonAsync("/v1/me/phone-changes", new { phone = newPhone }, ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync(ct));
        var verificationId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("verificationId").GetGuid();
        var confirmed = await customer.PostAsJsonAsync(
            $"/v1/me/phone-changes/{verificationId}/confirmation",
            new { code = _factory.Sms.LastCodeFor(PhoneNumbers.E164(newPhone)) }, ct);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var staff = Staff(StaffPermissions.CustomerView);
        var profile = await staff.GetFromJsonAsync<JsonElement>($"/v1/customers/by-account/{accountId}", ct);

        profile.GetProperty("phone").GetString().ShouldBe(MaskedPhone(newPhone));
        var change = profile.GetProperty("phoneChanges").EnumerateArray().ShouldHaveSingleItem();
        change.GetProperty("oldPhone").GetString().ShouldBe(MaskedPhone(oldPhone));
        change.GetProperty("newPhone").GetString().ShouldBe(MaskedPhone(newPhone));
        change.GetProperty("changedAt").GetDateTimeOffset().ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task IzinsizCalisan_VeMusteri_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, accountId, _) = await OnboardingFlows.RegisteredAsync(_factory, ct);
        using var unauthorized = Staff(StaffPermissions.WithdrawalReview);
        using var customer = _factory.CreateClient().As(subject);

        (await unauthorized.GetAsync($"/v1/customers/by-account/{accountId}", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await unauthorized.PostAsJsonAsync("/v1/customer-searches", new { email = "a@ornek.com" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Müşteri kendi bilgisini /v1/me'den görüyor; bu uçlar yalnızca çalışana.
        (await customer.GetAsync($"/v1/customers/by-account/{accountId}", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await customer.PostAsJsonAsync("/v1/customer-searches", new { email = "a@ornek.com" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Onboarding'den açılmamış hesap (işyeri) ya da olmayan hesap.</summary>
    [Fact]
    public async Task KaydiOlmayanHesap_404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var staff = Staff(StaffPermissions.CustomerView);

        (await staff.GetAsync($"/v1/customers/by-account/{Guid.NewGuid()}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Arama_EpostaTelefonVeKimlikNumarasiyla()
    {
        var ct = TestContext.Current.CancellationToken;
        var nationalId = NationalIds.New();
        var (_, accountId, email, phone) = await OnboardingFlows.VerifiedAsync(_factory, ct, nationalId: nationalId);
        using var staff = Staff(StaffPermissions.CustomerView);

        // E-posta büyük harfle ve boşlukla da bulunuyor; telefon yazıldığı biçimden bağımsız.
        foreach (var criterion in new object[]
                 {
                     new { email = $" {email.ToUpperInvariant()} " },
                     new { phone = PhoneNumbers.E164(phone) },
                     new { nationalId }
                 })
        {
            var match = (await SearchAsync(staff, criterion, ct)).EnumerateArray().ShouldHaveSingleItem();
            match.GetProperty("accountId").GetGuid().ShouldBe(accountId);
            match.GetProperty("email").GetString().ShouldBe(email);
            match.GetProperty("firstName").GetString().ShouldBe("Ayşe");
            match.GetProperty("phone").GetString().ShouldBe(MaskedPhone(phone));
        }
    }

    [Fact]
    public async Task Arama_EslesmeYoksaBosListe()
    {
        var ct = TestContext.Current.CancellationToken;
        using var staff = Staff(StaffPermissions.CustomerView);

        (await SearchAsync(staff, new { email = $"yok-{Guid.NewGuid():N}@ornek.com" }, ct)).GetArrayLength().ShouldBe(0);
        (await SearchAsync(staff, new { nationalId = NationalIds.New() }, ct)).GetArrayLength().ShouldBe(0);
        (await SearchAsync(staff, new { phone = PhoneNumbers.New() }, ct)).GetArrayLength().ShouldBe(0);
    }

    /// <summary>Tek ölçüt, geçerli biçimde: kurala uymayan numara aranacak bir şey değil.</summary>
    [Fact]
    public async Task Arama_TekVeGecerliOlcut_Degilse400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var staff = Staff(StaffPermissions.CustomerView);

        foreach (var criterion in new object[]
                 {
                     new { },
                     new { email = "a@ornek.com", phone = PhoneNumbers.New() },
                     new { nationalId = "12345678901" },
                     new { phone = "12345" },
                     new { email = "e-posta-degil" }
                 })
        {
            var response = await staff.PostAsJsonAsync("/v1/customer-searches", criterion, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, JsonSerializer.Serialize(criterion));
        }
    }
}
