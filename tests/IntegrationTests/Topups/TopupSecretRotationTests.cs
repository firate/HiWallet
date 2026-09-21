using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.TopupWebhook.Application;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Secret rotasyonu. Sözleşme bu hatta bizim olduğu için değeri de biz üretiyoruz,
/// ama sağlayıcının yeni değeri kendi sistemine yayması zaman alıyor; o aralıkta
/// gelen webhook'lar eski secret ile imzalı geliyor. İki değer aynı anda geçerli
/// olmazsa para yükleme bildirimleri <c>401</c> alır ve inbox'a hiç girmez.
///
/// Geçersiz kılma ayrı bir mekanizma değil: secret listeden çıkarılır
/// (bank-webhook ile aynı karar, decisions.md madde 35).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TopupSecretRotationTests(InboxFixture inbox)
{
    private const string YeniSecret = "yeni-webhook-secret";
    private const string EskiSecret = "eski-webhook-secret";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static object Payload(string eventId) => new
    {
        eventId,
        walletId = Guid.NewGuid(),
        amount = 100.00m,
        currency = "TRY",
        reference = "pi_test",
        occurredAt = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00")
    };

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, object payload, string secret)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/v1/webhooks/topup/{TopupWebhookApiFactory.StripeProvider}")
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(WebhookSignature.HeaderName, WebhookSignature.Compute(body, secret));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Pencerenin asıl konusu: sağlayıcı hâlâ eskisiyle imzalıyor ve bildirim kabul ediliyor.
    /// </summary>
    [Fact]
    public async Task RotasyonPenceresinde_EskiSecretIleImzaliBildirim_KabulEdilir()
    {
        await using var factory = new TopupWebhookApiFactory(inbox, [YeniSecret, EskiSecret]);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, Payload($"evt-{Guid.NewGuid():N}"), EskiSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task RotasyonPenceresinde_YeniSecretIleImzaliBildirim_KabulEdilir()
    {
        await using var factory = new TopupWebhookApiFactory(inbox, [YeniSecret, EskiSecret]);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, Payload($"evt-{Guid.NewGuid():N}"), YeniSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// Geçersiz kılma. Eski secret listeden çıkınca onunla imzalanmış bildirim geçmiyor.
    /// </summary>
    [Fact]
    public async Task EskiSecretListedenCikinca_OnunlaImzaliBildirim_401Doner()
    {
        await using var factory = new TopupWebhookApiFactory(inbox, [YeniSecret]);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, Payload($"evt-{Guid.NewGuid():N}"), EskiSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
