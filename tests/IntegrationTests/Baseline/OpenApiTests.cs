using System.Net;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HiWallet.IntegrationTests.Baseline;

/// <summary>
/// OpenAPI dokümanı ve Scalar arayüzü (baseline.md madde 9).
///
/// İki şey sınanıyor: doküman GERÇEKTEN üretiliyor mu (üreteç değişti, Swashbuckle
/// gitti) ve Development kapısı gerçekten kapanıyor mu. İkincisi asıl önemli olan —
/// kapı <c>MapHiWalletOpenApi</c>'nin içinde ve unutulduğunda hiçbir şey kırılmaz,
/// yalnızca üretimde API yüzeyinin şeması sessizce yayınlanır.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OpenApiTests(PostgresFixture postgres) : IAsyncLifetime
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

    /// <summary>
    /// Doküman üretiliyor ve controller'ları gerçekten görüyor. Yalnızca <c>200</c>
    /// beklemek yetmezdi: boş bir doküman da <c>200</c> döner.
    /// </summary>
    [Fact]
    public async Task Dokuman_UretiliyorVeUclariIceriyor()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/openapi/v1.json", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/v1/transfers", out _).ShouldBeTrue();
        paths.TryGetProperty("/v1/accounts", out _).ShouldBeTrue();
    }

    /// <summary>
    /// Eğik çizgisiz <c>/scalar</c> sayfayı DÖNMÜYOR, <c>/scalar/</c>'a yönlendiriyor.
    /// Arayüz göreli varlık yüklüyor; eğik çizgi olmadan o varlıkların yolu bir seviye
    /// yukarıdan çözülürdü.
    ///
    /// Yönlendirmeyi takip ETMEDEN sınamak şart: <c>CreateClient()</c> varsayılan
    /// olarak takip ediyor, takip edilince test <c>200</c> görüp geçiyor —
    /// dokümandaki adresi <c>curl</c> ile deneyen ise <c>302</c> alıyor. Bu testin
    /// ilk hali tam olarak bunu gizlemişti.
    /// </summary>
    [Fact]
    public async Task Scalar_EgikCizgiyeYonlendiriyor()
    {
        var ct = TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/scalar", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().ShouldBe("scalar/");
    }

    /// <summary>Yönlendirmenin ucundaki sayfa gerçekten açılıyor.</summary>
    [Fact]
    public async Task ScalarArayuzu_Aciliyor()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/scalar/", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    /// <summary>
    /// <b>Asıl kanıt.</b> Development dışında iki uç da YOK. Şemayı yayınlamak
    /// saldırgana hangi ucun var olduğunu, hangi alanları beklediğini ve hangi
    /// doğrulamaların çalıştığını hazır veriyor.
    /// </summary>
    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar")]
    [InlineData("/scalar/")]
    public async Task ProductionDa_UcYok(string path)
    {
        var ct = TestContext.Current.CancellationToken;

        using var production = _factory.WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));

        // Yönlendirme takip EDİLMİYOR: takip edilseydi bir yönlendirme zinciri
        // sonunda 404'e varır ve uç "yok" görünürdü — oysa ilk uç var demektir.
        using var client = production.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
