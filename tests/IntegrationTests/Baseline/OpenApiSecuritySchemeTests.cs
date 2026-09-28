using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HiWallet.IntegrationTests.Baseline;

/// <summary>
/// Dokümanın sunduğu giriş yolu API'yi kimin çağırdığına göre: mobil ön API'yi
/// tarayıcıda giriş yapan müşteri, business-api'yi işyerinin sistemi çağırıyor.
/// Diğer yolu sunsa Scalar'dan alınan token ön API'de 401 alırdı.
/// </summary>
public sealed class OpenApiSecuritySchemeTests
{
    private static async Task<JsonElement> DocumentAsync<T>(WebApplicationFactory<T> factory, CancellationToken ct)
        where T : class
    {
        using var client = factory.CreateClient();
        return JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", ct)).RootElement;
    }

    private static JsonElement Flows(JsonElement document) =>
        document.GetProperty("components").GetProperty("securitySchemes").GetProperty("keycloak").GetProperty("flows");

    [Fact]
    public async Task MobilOnApi_YalnizcaTarayicidaGiris()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PersonalMobileApiFactory();

        var flows = Flows(await DocumentAsync(factory, ct));

        flows.TryGetProperty("authorizationCode", out _).ShouldBeTrue();
        flows.TryGetProperty("clientCredentials", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task BusinessApi_YalnizcaClientCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new BusinessApiFactory();

        var flows = Flows(await DocumentAsync(factory, ct));

        flows.TryGetProperty("clientCredentials", out _).ShouldBeTrue();
        flows.TryGetProperty("authorizationCode", out _).ShouldBeFalse();
    }

    /// <summary>Sahte sağlayıcı kimlik istemiyor; dokümanı da istemiyor gibi göstermiyor.</summary>
    [Fact]
    public async Task KimlikIstemeyenServis_SemaTasimiyor()
    {
        var ct = TestContext.Current.CancellationToken;
        using var topupWebhook = new HttpClient();
        await using var factory = new StripeFakeFactory(topupWebhook);

        var document = await DocumentAsync(factory, ct);

        document.TryGetProperty("security", out _).ShouldBeFalse();
        (document.TryGetProperty("components", out var components)
            && components.TryGetProperty("securitySchemes", out _)).ShouldBeFalse();
    }
}
