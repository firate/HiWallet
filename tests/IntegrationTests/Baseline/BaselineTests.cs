using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.Baseline;

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
        var checks = body.GetProperty("checks").EnumerateArray().ToList();

        // Transfer çekirdeği Postgres'e bağlı; o sağlıklı olmadan servis hazır değil.
        checks.Single(c => c.GetProperty("name").GetString() == "postgres")
            .GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task Readiness_BrokerBagimliligiIcERMEZ()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/health/ready", ct);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var checks = body.GetProperty("checks").EnumerateArray().ToList();

        // wallet-api'nin RabbitMQ ile hiç işi yok: top-up tüketicisi ayrı bir
        // deployable (decisions.md madde 28). Önceden burada Degraded dönen bir
        // broker kontrolü vardı; artık kontrol de bağımlılık da yok — bir broker
        // arızasının bu servise dokunamaması yapısal bir gerçek.
        checks.ShouldNotContain(c => c.GetProperty("name").GetString() == "rabbitmq");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task Readiness_IcDetaySizdirmaz()
    {
        var ct = TestContext.Current.CancellationToken;

        var raw = await _client.GetStringAsync("/health/ready", ct);

        // Bağlantı dizesi ya da parola response'ta görünmemeli.
        raw.ShouldNotContain("Password", Case.Insensitive);
        raw.ShouldNotContain("Host=", Case.Insensitive);
    }
}
