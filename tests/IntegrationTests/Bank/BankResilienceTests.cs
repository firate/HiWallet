using System.Net;
using HiWallet.BankAdapter.Application;
using HiWallet.BankAdapter.Setup;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiWallet.IntegrationTests.Bank;

/// <summary>
/// Adaptörün banka çağrısındaki resilience davranışı (baseline.md madde 11).
///
/// Bankaya giden çağrı bizim tek dış HTTP bağımlılığımız. Geçici hata iki kademede
/// karşılanıyor: önce process içinde yeniden deneme, o da tükenirse
/// <see cref="TransientBankException"/> ile mesajın kuyruğa geri dönmesi.
///
/// POST yeniden denenebiliyor çünkü her transfer <c>Idempotency-Key</c> taşıyor ve
/// banka aynı anahtarla ikinci transfer açmıyor (decisions.md madde 35).
///
/// Postgres ve RabbitMQ gerekmiyor: kurulan tek şey adaptörün <c>HttpClient</c>
/// kaydı, üstelik uygulamanın kullandığı metodun kendisiyle.
/// </summary>
public sealed class BankResilienceTests
{
    private const string Iban = "TR330006100519786457841326";

    private static StartTransferRequest Request() =>
        new(Guid.NewGuid().ToString(), 100m, "TRY", Iban);

    /// <summary>
    /// Adaptörün banka istemcisi, birincil handler'ı senaryo handler'ıyla
    /// değiştirilmiş halde. Retry ve circuit breaker onun ÜSTÜNDE kalıyor.
    /// </summary>
    private static ServiceProvider BuildClient(ScriptedBankHandler handler)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        services.AddSingleton<IOptions<BankAdapterOptions>>(Options.Create(
            new BankAdapterOptions
            {
                BaseUrl = "http://bank-fake",
                // Testte kısa: bekleme süresi değil, deneme SAYISI sınanıyor.
                RequestTimeout = TimeSpan.FromSeconds(2)
            }));

        services.AddBankHttpClient()
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        services.AddSingleton<BankClient>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Bankanın tek seferlik <c>503</c>'ü çağıran tarafa hiç ulaşmıyor: pipeline aynı
    /// request'i yeniden gönderiyor ve ikinci cevap dönüyor. Bu olmadan her anlık
    /// kesinti mesajı kuyruğa geri atardı.
    /// </summary>
    [Fact]
    public async Task GeciciHata_ProcessIcinde_YenidenDeneniyor()
    {
        var ct = TestContext.Current.CancellationToken;

        using var handler = new ScriptedBankHandler(
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.Accepted);

        await using var provider = BuildClient(handler);

        var accepted = await provider.GetRequiredService<BankClient>()
            .StartTransferAsync(Request(), Guid.NewGuid(), ct);

        accepted.Status.ShouldBe("pending");
        handler.Calls.ShouldBe(2, "ilk 503 yeniden denenmeli");
    }

    /// <summary>
    /// Denemeler tükendiğinde dışarı çıkan şey <see cref="TransientBankException"/>.
    ///
    /// Pipeline'ın kendi istisnaları buradan SIZMAMALI: tüketici yalnızca geçici ve
    /// kalıcı hatayı tanıyor, tanımadığı bir istisnada mesajı dead-letter'a yollardı —
    /// oysa banka yalnızca cevap vermedi.
    /// </summary>
    [Fact]
    public async Task DenemelerTukendiginde_TransientBankException_Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        using var handler = new ScriptedBankHandler(HttpStatusCode.ServiceUnavailable);

        await using var provider = BuildClient(handler);

        var client = provider.GetRequiredService<BankClient>();

        await Should.ThrowAsync<TransientBankException>(
            async () => await client.StartTransferAsync(Request(), Guid.NewGuid(), ct));

        handler.Calls.ShouldBeGreaterThan(1, "tek denemede pes edilmemeli");
    }

    /// <summary>
    /// Kalıcı hata yeniden DENENMİYOR: <c>400</c> ikinci denemede de <c>400</c> döner,
    /// aradaki bekleme yalnızca mesajı geciktirirdi.
    /// </summary>
    [Fact]
    public async Task KaliciHata_YenidenDenenmiyor()
    {
        var ct = TestContext.Current.CancellationToken;

        using var handler = new ScriptedBankHandler(HttpStatusCode.BadRequest);

        await using var provider = BuildClient(handler);

        var client = provider.GetRequiredService<BankClient>();

        await Should.ThrowAsync<PermanentBankException>(
            async () => await client.StartTransferAsync(Request(), Guid.NewGuid(), ct));

        handler.Calls.ShouldBe(1, "4xx tek denemede bitmeli");
    }

    /// <summary>
    /// Durum sorgusu da aynı pipeline'dan geçiyor. Mutabakat taramasının tek
    /// bilgi kaynağı bu endpoint; anlık bir kesintide tur boş dönerdi.
    /// </summary>
    [Fact]
    public async Task DurumSorgusu_GeciciHatada_YenidenDeneniyor()
    {
        var ct = TestContext.Current.CancellationToken;

        using var handler = new ScriptedBankHandler(
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.NotFound);

        await using var provider = BuildClient(handler);

        var status = await provider.GetRequiredService<BankClient>()
            .GetStatusAsync("BNK-1", ct);

        status.ShouldBeNull("404 bankanın kalıcı cevabı: referans tanınmıyor");
        handler.Calls.ShouldBe(2);
    }
}
