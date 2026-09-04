using System.Text;
using System.Text.Json;
using FluentValidation;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.TopupWebhook.Api.Requests;
using HiWallet.TopupWebhook.Application;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.TopupWebhook.Api.Controllers;

[ApiController]
[Route("v1/webhooks/topup")]
public sealed class TopupWebhookController(
    WebhookSecrets secrets,
    IValidator<TopupWebhookPayload> validator,
    TopupInboxWriter inbox,
    ILogger<TopupWebhookController> logger) : ControllerBase
{
    /// <summary>
    /// Gövde sınırı. İmza gövdenin tamamı üzerinde hesaplandığı için sınırsız gövde
    /// doğrudan CPU tüketimi demek; kimliği doğrulanmamış bir uçta bu bir saldırı yüzeyi.
    /// </summary>
    private const int MaxBodyBytes = 32 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sağlayıcı webhook'u. Sıra: imza → parse → doğrulama → inbox → 200.
    ///
    /// <b>İmza en başta</b>, parse'tan bile önce: doğrulanmamış gövdeyi parse etmek
    /// saldırganın kontrolündeki veriyi işlemeye başlamak olurdu.
    ///
    /// <b>200 en sonda</b>, commit'ten sonra: sağlayıcıya "aldım" demek "kaybetmem"
    /// sözü vermektir. Çoğu sağlayıcı 200 aldıktan sonra bir daha denemiyor.
    /// </summary>
    [HttpPost("{provider}")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(string provider, CancellationToken ct)
    {
        // Tanınmayan sağlayıcı da 401: 404 dönmek hangi sağlayıcıların tanımlı
        // olduğunu dışarıya söylerdi.
        if (!secrets.TryGet(provider, out var secret))
        {
            logger.LogWarning("Tanınmayan sağlayıcıdan webhook: {Provider}", provider);
            return Unauthorized();
        }

        var body = await ReadBodyAsync(ct);

        if (!WebhookSignature.IsValid(body, secret, Request.Headers[WebhookSignature.HeaderName]))
        {
            logger.LogWarning("Geçersiz webhook imzası. Sağlayıcı {Provider}", provider);
            return Unauthorized();
        }

        TopupWebhookPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<TopupWebhookPayload>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            return Problem(
                title: "Gövde çözümlenemedi.",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (payload is null)
        {
            return Problem(title: "Gövde boş.", statusCode: StatusCodes.Status400BadRequest);
        }

        var validation = await validator.ValidateAsync(payload, ct);

        if (!validation.IsValid)
        {
            // Model binding kullanılmadığı için wallet-service'teki ValidationFilter
            // burada devreye girmiyor: gövde ham bayt olarak okunuyor (imza için) ve
            // elle parse ediliyor. Doğrulama da bu yüzden elle çağrılıyor.
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var message = new TopupReceived
        {
            Provider = provider,
            EventId = payload.EventId!,
            LedgerAccountId = payload.WalletId!.Value,
            Amount = payload.Amount!.Value,
            Currency = payload.Currency!.ToUpperInvariant(),
            ProviderRef = payload.Reference!,
            OccurredAt = payload.OccurredAt!.Value
        };

        var duplicate = await inbox.WriteAsync(message, Encoding.UTF8.GetString(body), ct);

        if (duplicate)
        {
            logger.LogInformation(
                "Tekrar eden webhook, inbox'a yazılmadı. {Provider}/{EventId}", provider, message.EventId);
        }

        // Tekrar da başarı: sağlayıcının yeniden göndermesi beklenen bir davranış,
        // hata değil. Hata dönmek sağlayıcıyı sonsuz tekrara sokardı.
        return Ok(new { received = true, duplicate });
    }

    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        // Gövde ham bayt olarak okunuyor: imza tam olarak gelen baytlar üzerinde.
        // Deserialize edip yeniden serialize edilmiş JSON'da boşluk ve alan sırası
        // değişir, imza tutmaz.
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);

        return buffer.ToArray();
    }
}
