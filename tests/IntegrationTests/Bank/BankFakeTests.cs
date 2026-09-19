using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.Bank.Fake.Application;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.Bank;

/// <summary>
/// Sahte bankanın HTTP davranışı. Bankanın yerinde duran taraf bu; sınanan şey
/// bizim kodumuz değil, taklidin gerçekçi olması.
///
/// Gerçekçilik burada bir test kolaylığı değil ZORUNLULUK: adaptörün "kabul edildi
/// ama sonuç sonra" akışını doğru kurduğunu ancak karşı taraf gerçekten öyle
/// davranırsa kanıtlayabilirsin (decisions.md madde 35).
///
/// Postgres koleksiyonunda DEĞİL: sahte bankanın veritabanı yok, bu testler
/// Postgres ve RabbitMQ olmadan koşuyor.
/// </summary>
public sealed class BankFakeTests : IAsyncLifetime
{
    private const string Iban = "TR330006100519786457841326";

    private BankFakeFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new BankFakeFactory();
        _client = _factory.CreateClient();

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<HttpResponseMessage> StartAsync(
        string clientReference, string idempotencyKey, decimal amount = 100m)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(new
            {
                clientReference,
                amount,
                currency = "TRY",
                destinationIban = Iban
            })
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <b>Bu testin konusu tek bir kelime: <c>pending</c>.</b>
    ///
    /// Eski sahte servis sonucu aynı çağrıda dönüyordu ve bu yüzden saga'nın
    /// <c>bank_transfer_pending</c> durumu fiilen ölüydü — takılmış saga taraması
    /// hiç iş yapmıyordu. Gerçek havale "aldım" der, sonucu sonra bildirir.
    /// </summary>
    [Fact]
    public async Task Kabul_SonucDondurmuyor_PendingDonuyor()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await StartAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        body.GetProperty("status").GetString().ShouldBe("pending");
        body.GetProperty("bankReference").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("replayed").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// Anahtarsız request reddediliyor. Sahte de olsa bu kontrol şart: adaptörün
    /// anahtarı gerçekten gönderdiğini kanıtlayan tek şey bu.
    /// </summary>
    [Fact]
    public async Task AnahtarYok_400Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("/v1/transfers", new
        {
            clientReference = Guid.NewGuid().ToString(),
            amount = 100m,
            currency = "TRY",
            destinationIban = Iban
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        _factory.TransferCount().ShouldBe(0, "reddedilen istek transfer açmamalı");
    }

    /// <summary>
    /// Aynı anahtarla ikinci request YENİ transfer açmıyor — bankanın kendi koruması.
    /// Süreç bankayı arayıp kaydı yazmadan ölse bile tekrar teslimde aynı transferi
    /// geri alıyoruz; bu davranış olmasaydı o senaryoda para iki kez giderdi.
    /// </summary>
    [Fact]
    public async Task AyniAnahtar_AyniTransferiDoner()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientReference = Guid.NewGuid().ToString();
        var key = Guid.NewGuid().ToString();

        var first = await StartAsync(clientReference, key);
        var second = await StartAsync(clientReference, key);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(ct);

        secondBody.GetProperty("bankReference").GetString()
            .ShouldBe(firstBody.GetProperty("bankReference").GetString());

        firstBody.GetProperty("replayed").GetBoolean().ShouldBeFalse();
        secondBody.GetProperty("replayed").GetBoolean().ShouldBeTrue();

        _factory.TransferCount(clientReference).ShouldBe(1, "aynı anahtar ikinci transfer açmamalı");
    }

    /// <summary>
    /// Aynı anahtarla AYNI ANDA gelen request'ler de tek transfer açıyor. Sıralı
    /// tekrarı yukarıdaki test yakalıyor; bu, "önce bak sonra ekle" arasına başka
    /// bir request'in girebildiği yarışı yakalıyor. Veritabanı yokken o yarışı
    /// kapatan şey deponun kilidi.
    /// </summary>
    [Fact]
    public async Task AyniAnahtar_EszamanliIstekler_TekTransferAcar()
    {
        var clientReference = Guid.NewGuid().ToString();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => StartAsync(clientReference, key)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Accepted);
        _factory.TransferCount(clientReference).ShouldBe(1, "eşzamanlı tekrar ikinci transfer açmamalı");
    }

    /// <summary>
    /// Geçici hatada transfer HİÇ KABUL EDİLMİYOR: <c>503</c> ve ortada satır yok.
    ///
    /// Kalıcı hatadan farkı tam olarak bu. Karıştırılsaydı her ağ kesintisi
    /// müşterinin parasını ileri geri taşırdı (overview.md madde 6).
    /// </summary>
    [Fact]
    public async Task GeciciHata_503Doner_TransferAcilmaz()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientReference = Guid.NewGuid().ToString();

        await SetScenarioAsync(clientReference, TransferOutcome.TransientFailure, transientFailures: 1);

        var first = await StartAsync(clientReference, Guid.NewGuid().ToString());

        first.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        _factory.TransferCount(clientReference).ShouldBe(0, "geçici hatada transfer kaydı oluşmamalı");

        // Kota doldu: ikinci deneme kabul ediliyor. "Transient sonra başarılı"
        // senaryosu bu ve adaptörün yeniden denemesinin işe yaradığının kanıtı.
        var second = await StartAsync(clientReference, Guid.NewGuid().ToString());

        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// Sonuç gecikmeyle belli oluyor ve durum sorgusu onu gösteriyor. Mutabakat
    /// taramasının okuduğu endpoint bu; çalışmazsa kaçırılan callback kalıcı kayıp olur.
    /// </summary>
    [Fact]
    public async Task DurumSorgusu_GecikmeSonrasi_Sonuclanir()
    {
        var ct = TestContext.Current.CancellationToken;

        var start = await StartAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(ct);
        var reference = started.GetProperty("bankReference").GetString()!;

        var final = await PollUntilResolvedAsync(reference, ct);

        final.GetProperty("status").GetString().ShouldBe("succeeded");
        final.GetProperty("fee").GetDecimal().ShouldBeGreaterThan(0m);
        final.GetProperty("clientReference").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task KaliciHata_DurumSorgusundaFailed()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientReference = Guid.NewGuid().ToString();
        await SetScenarioAsync(clientReference, TransferOutcome.Failure);

        var start = await StartAsync(clientReference, Guid.NewGuid().ToString());
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(ct);

        var final = await PollUntilResolvedAsync(started.GetProperty("bankReference").GetString()!, ct);

        final.GetProperty("status").GetString().ShouldBe("failed");
        final.GetProperty("failureReason").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BilinmeyenReferans_404Doner()
    {
        var response = await _client.GetAsync("/v1/transfers/YOK-BOYLE-BIR-SEY",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task SetScenarioAsync(
        string clientReference, TransferOutcome outcome, int transientFailures = 1)
    {
        var response = await _client.PostAsJsonAsync("/v1/scenarios", new
        {
            clientReference,
            outcome = outcome.ToString(),
            transientFailures,
            delayMilliseconds = 0
        }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Sonuç belli olana kadar sorar. <c>SettlementDelay</c> testte 200 ms; üst
    /// sınır cömert tutuluyor ki yavaş bir CI makinesinde rastgele düşmesin.
    /// </summary>
    private async Task<JsonElement> PollUntilResolvedAsync(string reference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var response = await _client.GetAsync($"/v1/transfers/{reference}", ct);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

            if (body.GetProperty("status").GetString() is not "pending") return body;

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"{reference} beş saniyede sonuçlanmadı.");
    }
}
