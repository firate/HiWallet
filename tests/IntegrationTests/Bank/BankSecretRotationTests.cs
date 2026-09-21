using System.Net;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.Bank;

/// <summary>
/// Secret rotasyonu. Bankaya yeni secret iletildikten sonra karşı tarafın geçişi ne
/// zaman tamamlayacağını biz belirlemiyoruz; o aralıkta iki secret de geçerli olmak
/// zorunda. Eskisini erken kaldırmak bankanın callback'lerini <c>401</c>'e düşürür,
/// <c>bank_transfers</c> satırları <c>pending</c> kalır ve sonuç ancak mutabakat
/// taramasıyla öğrenilir.
///
/// Geçersiz kılma ayrı bir mekanizma değil: secret listeden çıkarılır.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BankSecretRotationTests(BankFixture bankDb)
{
    private const string YeniSecret = "yeni-callback-secret";
    private const string EskiSecret = "eski-callback-secret";

    private static byte[] Body(string eventId) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventId,
            bankReference = "BNK-ROT-0001",
            clientReference = Guid.NewGuid().ToString(),
            status = "succeeded",
            fee = 1.50m,
            currency = "TRY",
            occurredAt = DateTimeOffset.UtcNow
        });

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, byte[] body, string secret)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/v1/webhooks/bank/{TestBankSecrets.Bank}")
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TryAddWithoutValidation(
            "X-Bank-Signature", TestCallbackSigner.Sign(body, secret));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Pencerenin asıl konusu: banka hâlâ eskisiyle imzalıyor ve callback kabul ediliyor.
    /// </summary>
    [Fact]
    public async Task RotasyonPenceresinde_EskiSecretIleImzaliCallback_KabulEdilir()
    {
        await using var factory = new BankWebhookFactory(bankDb, [YeniSecret, EskiSecret]);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, Body($"evt-{Guid.NewGuid():N}"), EskiSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task RotasyonPenceresinde_YeniSecretIleImzaliCallback_KabulEdilir()
    {
        await using var factory = new BankWebhookFactory(bankDb, [YeniSecret, EskiSecret]);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, Body($"evt-{Guid.NewGuid():N}"), YeniSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// Geçersiz kılma. Eski secret listeden çıkınca onunla imzalanmış callback
    /// geçmiyor — ayrılan personelin bildiği secret'ın kapatılma yolu bu.
    /// </summary>
    [Fact]
    public async Task EskiSecretListedenCikinca_OnunlaImzaliCallback_401Doner()
    {
        await using var factory = new BankWebhookFactory(bankDb, [YeniSecret]);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, Body($"evt-{Guid.NewGuid():N}"), EskiSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
