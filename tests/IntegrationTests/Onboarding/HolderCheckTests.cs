using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Onboarding.Domain;
using HiWallet.WalletConsumer.Identity;

namespace HiWallet.IntegrationTests.Onboarding;

/// <summary>
/// Havalenin müşterinin kendi hesabından geldiğini doğrulamak için wallet'ın sorusu: bu
/// kimlik numarası bu hesap sahibinin mi. Cevap yalnızca evet ya da hayır; numara wallet'a
/// geri verilmiyor. Soran yalnızca wallet-consumer'ın istemcisi.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HolderCheckTests(PostgresFixture postgres, OnboardingFixture onboardingDb) : IAsyncLifetime
{
    private const string NationalIdValue = "10000000146";

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

    /// <summary>Kimliği nüfus kaydıyla doğrulanmış bir müşteri; numarası her testte ayrı.</summary>
    private async Task<(string Subject, string NationalId)> VerifiedCustomerAsync(CancellationToken ct)
    {
        var subject = $"sahip-{Guid.NewGuid():N}";
        var nationalId = NationalIds.New();
        var customer = Customer.New(subject, DateTimeOffset.UtcNow);
        customer.IdentityVerified("Ayşe", "Yılmaz", NationalId.Parse(nationalId), new DateOnly(1990, 5, 17), DateTimeOffset.UtcNow);

        await using var db = onboardingDb.CreateContext();
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        return (subject, nationalId);
    }

    private HttpClient WalletConsumer()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.For(
                $"service-account-{TestTokens.WalletConsumerClientId}",
                audiences: [TestTokens.InternalAudience],
                authorizedParty: TestTokens.WalletConsumerClientId));
        return client;
    }

    private static async Task<bool> MatchesAsync(HttpResponseMessage response, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("matches").GetBoolean();
    }

    [Fact]
    public async Task SahibinNumarasi_Evet_BaskasininkiHayir()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = await VerifiedCustomerAsync(ct);
        var other = await VerifiedCustomerAsync(ct);
        using var client = WalletConsumer();

        var own = await client.PostAsJsonAsync("/v1/holder-checks", new { holder = holder.Subject, nationalId = holder.NationalId }, ct);
        var someoneElses = await client.PostAsJsonAsync("/v1/holder-checks", new { holder = holder.Subject, nationalId = other.NationalId }, ct);

        (await MatchesAsync(own, ct)).ShouldBeTrue();
        (await MatchesAsync(someoneElses, ct)).ShouldBeFalse();
    }

    /// <summary>Kaydı olmayan sahip ve kurala uymayan numara da yalnızca "hayır".</summary>
    [Fact]
    public async Task BilinmeyenSahipVeBozukNumara_Hayir()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = WalletConsumer();

        var unknown = await client.PostAsJsonAsync("/v1/holder-checks", new { holder = "yok", nationalId = NationalIdValue }, ct);
        var malformed = await client.PostAsJsonAsync("/v1/holder-checks", new { holder = "yok", nationalId = "12345" }, ct);

        (await MatchesAsync(unknown, ct)).ShouldBeFalse();
        (await MatchesAsync(malformed, ct)).ShouldBeFalse();
    }

    /// <summary>Müşterinin ve başka bir servisin token'ı soramıyor: bu bir kimlik numarası sorgusu.</summary>
    [Fact]
    public async Task YalnizcaWalletConsumerSorabilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = await VerifiedCustomerAsync(ct);
        using var customer = _factory.CreateClient().As(holder.Subject);
        using var onboarding = _factory.CreateClient().AsOnboarding();
        var body = new { holder = holder.Subject, nationalId = holder.NationalId };

        (await customer.PostAsJsonAsync("/v1/holder-checks", body, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await onboarding.PostAsJsonAsync("/v1/holder-checks", body, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EksikAlan_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = WalletConsumer();

        (await client.PostAsJsonAsync("/v1/holder-checks", new { holder = "x" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// wallet-consumer'ın istemcisi onboarding'in sözleşmesini kendi tipleriyle yazıyor;
    /// iki taraf ayrıştığında burası kırılıyor.
    /// </summary>
    [Fact]
    public async Task WalletConsumerIstemcisi_AyniSozlesmeyiKonusur()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = await VerifiedCustomerAsync(ct);
        var identity = new OnboardingHolderIdentity(WalletConsumer());

        (await identity.IsHolderAsync(holder.Subject, holder.NationalId, ct)).ShouldBeTrue();
        (await identity.IsHolderAsync(holder.Subject, NationalIdValue, ct)).ShouldBeFalse();
    }

    /// <summary>Cevap alınamazsa "hayır" UYDURULMUYOR: hata yukarı çıkıyor, havale kuyruğa dönüyor.</summary>
    [Fact]
    public async Task WalletConsumerIstemcisi_YetkisizseHataVerir()
    {
        var ct = TestContext.Current.CancellationToken;
        var identity = new OnboardingHolderIdentity(_factory.CreateClient().AsOnboarding());

        await Should.ThrowAsync<HttpRequestException>(() => identity.IsHolderAsync("x", NationalIdValue, ct));
    }
}
