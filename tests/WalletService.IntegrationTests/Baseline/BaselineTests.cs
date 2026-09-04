using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.WalletService.IntegrationTests.Fixtures;

namespace HiWallet.WalletService.IntegrationTests.Baseline;

/// <summary>
/// baseline.md'nin katmanları GERÇEKTEN çalışıyor mu (madde: "gerçekten çalışıyor,
/// mock değil"). Bunlar dominant temayı değil, her serviste beklenen hijyeni sınıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BaselineTests(PostgresFixture postgres) : IAsyncLifetime
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

    [Fact]
    public async Task Liveness_BagimliliklaraBakmaz_VeAyaktaysaSaglikli()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/health/live", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("status").GetString().ShouldBe("Healthy");

        // Liveness hiçbir bağımlılık kontrol etmemeli: DB düştüğünde container'ın
        // yeniden başlatılması durumu düzeltmez, sadece kötüleştirir.
        body.GetProperty("checks").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Readiness_GercekBagimliligiKontrolEder()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/health/ready", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("status").GetString().ShouldBe("Healthy");

        var checks = body.GetProperty("checks").EnumerateArray().ToList();
        checks.ShouldContain(c => c.GetProperty("name").GetString() == "postgres");
    }

    [Fact]
    public async Task Readiness_IcDetaySizdirmaz()
    {
        var ct = TestContext.Current.CancellationToken;

        var raw = await _client.GetStringAsync("/health/ready", ct);

        // Bağlantı dizesi ya da parola yanıtta görünmemeli.
        raw.ShouldNotContain("Password", Case.Insensitive);
        raw.ShouldNotContain("Host=", Case.Insensitive);
    }

    [Fact]
    public async Task RateLimit_AsildigindaRetryAfterIle429Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        // BurstSize 20; aynı IP'den arka arkaya istek atınca kova boşalmalı.
        // İstekler geçersiz (400) olsa bile limiter'dan geçiyorlar — limiter
        // middleware, controller'dan önce.
        HttpResponseMessage? limited = null;

        for (var i = 0; i < 40 && limited is null; i++)
        {
            var response = await _client.PostAsJsonAsync("/v1/transfers", new
            {
                fromWalletId = Guid.Empty,
                toWalletId = Guid.Empty,
                amount = 0m,
                currency = "TRY",
                type = "P2P"
            }, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
        }

        limited.ShouldNotBeNull("40 istekte rate limit hiç devreye girmedi");
        limited.Headers.RetryAfter.ShouldNotBeNull("Retry-After yoksa client hemen tekrar dener");
        limited.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task HealthCheckler_RateLimitEDILMEZ()
    {
        var ct = TestContext.Current.CancellationToken;

        // Probe'un limite takılması sağlıklı bir servisi trafikten çektirir.
        for (var i = 0; i < 50; i++)
        {
            var response = await _client.GetAsync("/health/live", ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{i}. probe limite takıldı");
        }
    }
}
