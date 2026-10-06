using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HiWallet.Stripe.Fake.Payments;
using Microsoft.Extensions.Options;

namespace HiWallet.Stripe.Fake.Webhooks;

/// <summary>
/// Ödemenin sonucunu <c>topup-webhook</c>'a imzalı webhook olarak bildirir.
///
/// <b>İmza HAM gövde baytları üzerinde.</b> Nesneyi yeniden serialize edip imzalasaydık
/// boşluk ve alan sırası farkı doğrulamayı düşürürdü; doğrulayıcı tam olarak gelen
/// baytlara bakıyor.
///
/// Süresi dolan ödeme için webhook YOK: gerçek sağlayıcıların çoğu terk edilen oturumu
/// bildirmiyor. Kart yüklemesi servisi cevapsız kalan ödemeyi sağlayıcıya soruyor.
/// </summary>
public sealed class PaymentWebhookSender(
    IHttpClientFactory httpClientFactory,
    IOptions<StripeFakeOptions> options,
    TimeProvider time,
    ILogger<PaymentWebhookSender> logger)
{
    public const string HttpClientName = "topup-webhook";

    /// <summary>Bizim sözleşmemizin imza başlığı.</summary>
    private const string SignatureHeader = "X-Hive-Signature";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SendAsync(CardPayment payment, string type, CancellationToken ct)
    {
        var body = new PaymentWebhookBody
        {
            EventId = $"evt_{Guid.NewGuid():N}",
            Type = type,
            PaymentId = payment.Id,
            Reference = payment.Reference,
            Amount = payment.Amount,
            Currency = payment.Currency,
            OccurredAt = payment.DecidedAt ?? time.GetUtcNow()
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/webhooks/topup/{StripeFakeOptions.Provider}")
        {
            Content = content
        };

        request.Headers.TryAddWithoutValidation(SignatureHeader, Sign(bytes));

        using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);

        // Ret durum kodu istisna değil: sahte sağlayıcının işi göndermek, sonucu yorumlamak değil.
        logger.LogInformation(
            "Ödeme webhook'u gönderildi. {PaymentId} {Type} → {Status}", payment.Id, type, (int)response.StatusCode);
    }

    private string Sign(ReadOnlySpan<byte> body)
    {
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.WebhookSecret!), body, hash);

        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// <c>topup-webhook</c>'un beklediği gövde. Alan adları onun <c>TopupWebhookPayload</c>'ıyla
    /// eşleşmek zorunda; ayrışırsa doğrulama "eksik alan" der.
    /// </summary>
    private sealed record PaymentWebhookBody
    {
        public required string EventId { get; init; }

        public required string Type { get; init; }

        public required string PaymentId { get; init; }

        public required Guid Reference { get; init; }

        public required decimal Amount { get; init; }

        public required string Currency { get; init; }

        public required DateTimeOffset OccurredAt { get; init; }
    }
}

/// <summary>Gönderilecek bildirim: hangi ödeme, hangi olay.</summary>
public sealed record PaymentWebhook(CardPayment Payment, string Type);

/// <summary>
/// Bildirimleri arka planda, sırayla gönderir. Müşterinin "Öde"si webhook'u beklemeden
/// dönüyor; gerçek sağlayıcı da bildirimi sonra gönderiyor. Kalıcı DEĞİL: süreç ölürse
/// kuyruktaki kaybolur ve kart yüklemesi servisinin taraması sonucu sağlayıcıya sorarak
/// yine buluyor.
/// </summary>
internal sealed class PaymentWebhookWorker(
    Channel<PaymentWebhook> queue,
    PaymentWebhookSender sender,
    ILogger<PaymentWebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var webhook in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(webhook.Payment, webhook.Type, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Bir gönderimin hatası worker'ı öldürmüyor.
                logger.LogError(exception, "Ödeme webhook'u gönderilemedi. {PaymentId}", webhook.Payment.Id);
            }
        }
    }
}
