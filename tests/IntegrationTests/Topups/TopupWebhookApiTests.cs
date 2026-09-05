using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.TopupWebhook.Application;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Webhook ucu: imza doğrulama, inbox yazımı, idempotency (overview.md madde 5).
/// Uygulama gerçek haliyle ayakta — rate limiter, model doğrulama ve controller
/// zinciri dahil.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TopupWebhookApiTests(InboxFixture inbox) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private TopupWebhookApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new TopupWebhookApiFactory(inbox);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GecerliImza_InboxaYazilir_Ve202Doner()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = NewEventId();
        var walletId = Guid.NewGuid();

        var response = await PostAsync(Payload(eventId, walletId), ct: ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        result.GetProperty("duplicate").GetBoolean().ShouldBeFalse();

        await using var db = inbox.CreateContext();
        var row = await db.Inbox.SingleAsync(m => m.EventId == eventId, ct);

        row.Provider.ShouldBe(TopupWebhookApiFactory.StripeProvider);
        row.LedgerAccountId.ShouldBe(walletId);

        // Yayınlanmamış olarak duruyor: 202 dönmek broker'a ulaşmakla ilgili değil,
        // kalıcı olmakla ilgili. Taşımak relay'in işi.
        row.PublishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task HamGovde_OldugGibiSaklanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = NewEventId();

        // Bilerek "tuhaf" biçimlenmiş gövde: fazladan boşluk ve alan sırası farklı.
        // Sağlayıcıyla ihtilafta cevap bu satır olacağı için, gelen baytlar aynen
        // durmalı — yeniden serialize edilmiş hali değil.
        var raw = $$"""
                    {  "amount" : 250.00,
                       "eventId":"{{eventId}}",  "walletId":"{{Guid.NewGuid()}}",
                       "currency":"TRY","reference":"pi_x","occurredAt":"2026-03-01T10:00:00+00:00"}
                    """;

        var response = await PostRawAsync(raw, TopupWebhookApiFactory.StripeSecret, ct: ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await using var db = inbox.CreateContext();
        var row = await db.Inbox.SingleAsync(m => m.EventId == eventId, ct);

        // jsonb kolonu boşlukları normalize ediyor; içerik olarak eşit olmalı.
        using var stored = JsonDocument.Parse(row.RawPayload);
        stored.RootElement.GetProperty("reference").GetString().ShouldBe("pi_x");
        stored.RootElement.GetProperty("amount").GetDecimal().ShouldBe(250.00m);
    }

    [Fact]
    public async Task ImzaBasligiYok_401_VeInboxaYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = NewEventId();

        var json = JsonSerializer.Serialize(Payload(eventId, Guid.NewGuid()), JsonOptions);
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/v1/webhooks/topup/{TopupWebhookApiFactory.StripeProvider}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await ShouldNotBeInInboxAsync(eventId, ct);
    }

    [Fact]
    public async Task YanlisSecretIleImzalanmis_401_VeInboxaYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = NewEventId();

        var response = await PostAsync(Payload(eventId, Guid.NewGuid()), secret: "yanlis-sir", ct: ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await ShouldNotBeInInboxAsync(eventId, ct);
    }

    [Fact]
    public async Task TaninmayanSaglayici_404_DEGIL_401_Doner()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = NewEventId();

        var response = await PostAsync(Payload(eventId, Guid.NewGuid()), provider: "yok-boyle-bir-saglayici", ct: ct);

        // 404 dönmek hangi sağlayıcıların tanımlı olduğunu dışarıya söylerdi.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await ShouldNotBeInInboxAsync(eventId, ct);
    }

    [Fact]
    public async Task AyniEvent_IkiKezGelirse_InboxtaTekSatirKalir()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = NewEventId();
        var payload = Payload(eventId, Guid.NewGuid());

        var first = await PostAsync(payload, ct: ct);
        var second = await PostAsync(payload, ct: ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Tekrar da başarı: sağlayıcı için yeniden gönderim beklenen davranış.
        // Hata dönmek onu sonsuz tekrara sokardı.
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(ct);

        firstBody.GetProperty("duplicate").GetBoolean().ShouldBeFalse();
        secondBody.GetProperty("duplicate").GetBoolean().ShouldBeTrue();

        await using var db = inbox.CreateContext();
        (await db.Inbox.CountAsync(m => m.EventId == eventId, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task AyniEvent_EsZamanliGelirse_YineTekSatir()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = NewEventId();
        var payload = Payload(eventId, Guid.NewGuid());

        // "Önce SELECT sonra INSERT" olsaydı burası kırılırdı: iki istek de
        // "yok" görüp ikisi de yazmaya çalışırdı. ON CONFLICT DO NOTHING kararı
        // tek adımda DB'ye verdiriyor.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => PostAsync(payload, ct: ct)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Accepted);

        await using var db = inbox.CreateContext();
        (await db.Inbox.CountAsync(m => m.EventId == eventId, ct)).ShouldBe(1);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task EksikAlan_400_Doner()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = NewEventId();

        // amount yok.
        var raw = $$"""
                    {"eventId":"{{eventId}}","walletId":"{{Guid.NewGuid()}}",
                     "currency":"TRY","reference":"pi_x","occurredAt":"2026-03-01T10:00:00+00:00"}
                    """;

        var response = await PostRawAsync(raw, TopupWebhookApiFactory.StripeSecret, ct: ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldNotBeInInboxAsync(eventId, ct);
    }

    [Fact]
    public async Task BozukJson_400_Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await PostRawAsync("{bu json degil", TopupWebhookApiFactory.StripeSecret, ct: ct);

        // İmza geçerli ama gövde bozuk: 401 değil 400. İkisi karışmamalı — 401
        // "sen kimsin", 400 "ne dediğini anlamadım".
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static string NewEventId() => $"evt_{Guid.NewGuid():N}";

    private static object Payload(string eventId, Guid walletId) => new
    {
        eventId,
        walletId,
        amount = 100.00m,
        currency = "TRY",
        reference = "pi_test",
        occurredAt = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00")
    };

    private Task<HttpResponseMessage> PostAsync(
        object payload,
        string? secret = null,
        string? provider = null,
        CancellationToken ct = default) =>
        PostRawAsync(
            JsonSerializer.Serialize(payload, JsonOptions),
            secret ?? TopupWebhookApiFactory.StripeSecret,
            provider,
            ct);

    private async Task<HttpResponseMessage> PostRawAsync(
        string json,
        string secret,
        string? provider = null,
        CancellationToken ct = default)
    {
        var body = Encoding.UTF8.GetBytes(json);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/webhooks/topup/{provider ?? TopupWebhookApiFactory.StripeProvider}")
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(WebhookSignature.HeaderName, WebhookSignature.Compute(body, secret));

        return await _client.SendAsync(request, ct);
    }

    private async Task ShouldNotBeInInboxAsync(string eventId, CancellationToken ct)
    {
        await using var db = inbox.CreateContext();

        (await db.Inbox.AnyAsync(m => m.EventId == eventId, ct))
            .ShouldBeFalse("reddedilen istek inbox'a yazılmamalı");
    }
}
