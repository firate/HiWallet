using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.BackofficeBff;
using HiWallet.BusinessApi;
using HiWallet.BusinessWebBff;
using HiWallet.PersonalMobileApi;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HiWallet.IntegrationTests.Baseline;

/// <summary>
/// Ön API'ler ayağa kalkıyor ve sağlık uçları çalışıyor.
///
/// Fabrikaya bağlantı dizesi VERİLMİYOR ve PostgresFixture kullanılmıyor: ön API'nin
/// veritabanı yok. Readiness listesinde <c>postgres</c> olmadığı ayrıca doğrulanıyor.
/// </summary>
public sealed class ApiHostHealthTests
{
    [Fact]
    public async Task PersonalMobileApi_SaglikUclariCalisiyor()
    {
        await using var factory = new WebApplicationFactory<PersonalMobileApiApp>();
        using var client = factory.CreateClient();

        await AssertHealthAsync(client);
    }

    [Fact]
    public async Task BusinessApi_SaglikUclariCalisiyor()
    {
        await using var factory = new WebApplicationFactory<BusinessApiApp>();
        using var client = factory.CreateClient();

        await AssertHealthAsync(client);
    }

    [Fact]
    public async Task BusinessWebBff_SaglikUclariCalisiyor()
    {
        await using var factory = new WebApplicationFactory<BusinessWebBffApp>();
        using var client = factory.CreateClient();

        await AssertHealthAsync(client);
    }

    [Fact]
    public async Task BackofficeBff_SaglikUclariCalisiyor()
    {
        await using var factory = new WebApplicationFactory<BackofficeBffApp>();
        using var client = factory.CreateClient();

        await AssertHealthAsync(client);
    }

    private static async Task AssertHealthAsync(HttpClient client)
    {
        var ct = TestContext.Current.CancellationToken;

        var live = await client.GetAsync("/health/live", ct);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);

        var ready = await client.GetAsync("/health/ready", ct);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await ready.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("checks").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ShouldNotContain("postgres");
    }
}
