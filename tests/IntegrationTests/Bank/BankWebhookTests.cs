using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Bank;

/// <summary>
/// Bankanın bizi çağırdığı uç. Sınanan şey tek bir cümle: doğrula, yaz, <c>202</c>.
///
/// Bu servisin işlemeyle ilgisi yok — transferi kapatmak <c>bank-adapter</c>'ın işi
/// (decisions.md madde 35). Buradaki testler o ayrımı da koruyor: inbox'a satır
/// düştüğünü doğruluyorlar, transferin kapandığını değil.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BankWebhookTests(BankFixture bankDb) : IAsyncLifetime
{
    private BankWebhookFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new BankWebhookFactory(bankDb);
        _client = _factory.CreateClient();

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static byte[] Body(string eventId, string status = "succeeded") =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventId,
            bankReference = "BNK-TEST-0001",
            clientReference = Guid.NewGuid().ToString(),
            status,
            fee = 1.50m,
            currency = "TRY",
            failureReason = (string?)null,
            occurredAt = DateTimeOffset.UtcNow
        });

    private async Task<HttpResponseMessage> PostAsync(
        byte[] body, string? signature, string bank = TestBankSecrets.Bank)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/webhooks/bank/{bank}")
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType = new("application/json");

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Bank-Signature", signature);
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GecerliImza_202Doner_InboxaYazar()
    {
        var ct = TestContext.Current.CancellationToken;

        var eventId = $"evt-{Guid.NewGuid():N}";
        var body = Body(eventId);

        var response = await PostAsync(body, TestCallbackSigner.Sign(body, TestBankSecrets.CallbackSecret));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        result.GetProperty("duplicate").GetBoolean().ShouldBeFalse();

        await using var db = bankDb.CreateContext();
        var row = await db.Callbacks.AsNoTracking().FirstOrDefaultAsync(c => c.EventId == eventId, ct);

        row.ShouldNotBeNull();
        row.ProcessedAt.ShouldBeNull("işleme bu servisin işi değil");
        row.RawPayload.ShouldBe(Encoding.UTF8.GetString(body), "ham gövde olduğu gibi saklanmalı");
    }

    /// <summary>
    /// Bankanın tekrar göndermesi BAŞARILI bir sonuç, hata değil. Hata dönmek
    /// bankayı gereksiz tekrara sokardı.
    /// </summary>
    [Fact]
    public async Task TekrarEdenBildirim_202Doner_IkinciSatirYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;

        var eventId = $"evt-{Guid.NewGuid():N}";
        var body = Body(eventId);
        var signature = TestCallbackSigner.Sign(body, TestBankSecrets.CallbackSecret);

        var first = await PostAsync(body, signature);
        var second = await PostAsync(body, signature);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        secondBody.GetProperty("duplicate").GetBoolean().ShouldBeTrue();

        await using var db = bankDb.CreateContext();
        (await db.Callbacks.CountAsync(c => c.EventId == eventId, ct)).ShouldBe(1);
    }

    /// <summary>
    /// İmza geçersizse <c>401</c> ve inbox'a HİÇBİR ŞEY yazılmıyor. Sahte callback
    /// gönderen biri, saga'yı istediği sonuca sürükleyebilirdi.
    /// </summary>
    [Theory]
    [InlineData(null, "imza başlığı yok")]
    [InlineData("sha256=deadbeef", "imza kısa")]
    [InlineData("bozuk", "önek yok")]
    public async Task GecersizImza_401Doner(string? signature, string reason)
    {
        var ct = TestContext.Current.CancellationToken;

        var eventId = $"evt-{Guid.NewGuid():N}";

        var response = await PostAsync(Body(eventId), signature);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, reason);

        await using var db = bankDb.CreateContext();
        (await db.Callbacks.CountAsync(c => c.EventId == eventId, ct)).ShouldBe(0);
    }

    /// <summary>
    /// Doğru imza AMA başka secret ile. Gövde bizim beklediğimiz biçimde olsa bile
    /// geçmemeli — sabit zamanlı karşılaştırmanın koruduğu şey bu.
    /// </summary>
    [Fact]
    public async Task BaskaSecretIleImza_401Doner()
    {
        var body = Body($"evt-{Guid.NewGuid():N}");

        var response = await PostAsync(body, TestCallbackSigner.Sign(body, "yanlis-secret"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Tanınmayan kurum da <c>401</c>, <c>404</c> değil: hangi bankalarla
    /// çalıştığımız dışarıya sızmamalı.
    /// </summary>
    [Fact]
    public async Task TaninmayanKurum_401Doner()
    {
        var body = Body($"evt-{Guid.NewGuid():N}");

        var response = await PostAsync(
            body, TestCallbackSigner.Sign(body, TestBankSecrets.CallbackSecret), bank: "baska-banka");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// <c>eventId</c>'siz bildirim <c>400</c>. Bu, "gövdeyi çözümleme" kuralının tek
    /// istisnası ve gerekçesi var: kimliksiz bir bildirim deduplike EDİLEMEZ, yani
    /// bankanın tekrarı ikinci kez işlenirdi.
    /// </summary>
    [Fact]
    public async Task EventIdYok_400Doner()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { bankReference = "BNK-1", status = "succeeded" });

        var response = await PostAsync(body, TestCallbackSigner.Sign(body, TestBankSecrets.CallbackSecret));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
