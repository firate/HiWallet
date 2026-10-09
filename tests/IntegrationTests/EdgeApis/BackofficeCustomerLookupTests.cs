using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// backoffice-bff'ten müşterinin kişisel bilgisi: arkasında gerçek onboarding ve wallet-api.
/// İzni onboarding kontrol ediyor; BFF reddi ve cevabı aynen aktarıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackofficeCustomerLookupTests(PostgresFixture postgres, OnboardingFixture onboardingDb)
    : IAsyncLifetime
{
    private WalletApiFactory _walletApi = null!;
    private OnboardingApiFactory _onboarding = null!;
    private BackofficeBffFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _walletApi = new WalletApiFactory(postgres);
        _onboarding = new OnboardingApiFactory(onboardingDb, new PassthroughHandler(_walletApi.CreateClient()));
        _factory = new BackofficeBffFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            onboarding: new PassthroughHandler(_onboarding.CreateClient()));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _onboarding.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private HttpClient Staff(params string[] permissions) =>
        _factory.CreateClient().WithCsrfHeader().SignedInAs($"calisan-{Guid.NewGuid():N}", permissions);

    [Fact]
    public async Task Destek_MusteriyiKimlikNumarasiylaBulur_BilgileriniGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        var nationalId = NationalIds.New();
        var (_, accountId, email, _) = await OnboardingFlows.VerifiedAsync(_onboarding, ct, nationalId: nationalId);
        using var support = Staff(TestStaff.Support);

        var search = await support.PostAsJsonAsync("/v1/customer-searches", new { nationalId }, ct);

        search.StatusCode.ShouldBe(HttpStatusCode.OK, await search.Content.ReadAsStringAsync(ct));
        var match = (await search.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        match.GetProperty("accountId").GetGuid().ShouldBe(accountId);

        var profile = await support.GetFromJsonAsync<JsonElement>($"/v1/customers/by-account/{accountId}", ct);

        profile.GetProperty("email").GetString().ShouldBe(email);
        profile.GetProperty("nationalId").GetString().ShouldBe($"{nationalId[..2]}*******{nationalId[^2..]}");
        profile.GetProperty("consents").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task IcServisinReddiAynenGeliyor()
    {
        var ct = TestContext.Current.CancellationToken;
        using var support = Staff(TestStaff.Support);
        using var unauthorized = Staff(StaffPermissions.WithdrawalReview);

        (await support.GetAsync($"/v1/customers/by-account/{Guid.NewGuid()}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await support.PostAsJsonAsync("/v1/customer-searches", new { }, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unauthorized.PostAsJsonAsync("/v1/customer-searches", new { email = "a@ornek.com" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
