using System.Text;
using System.Text.Json;
using FluentValidation;
using HiWallet.Shared.Contracts.Settlements;
using HiWallet.TopupWebhook.Api.Requests;
using HiWallet.TopupWebhook.Application;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.TopupWebhook.Api.Controllers;

/// <summary>
/// Sağlayıcının batch ödeme bildirimi (<c>decisions.md</c> madde 13, adım 5.5).
///
/// <b>Neden bu serviste.</b> Settlement de sağlayıcıdan gelen, imzalı, IP kısıtlı
/// bir bildirim — top-up webhook'uyla AYNI ağ maruziyeti. Ayrı bir uç açmak ya da
/// wallet-api'ye sağlayıcı yüzeyi eklemek madde 28'i delerdi. İmza doğrulama,
/// inbox ve relay olduğu gibi paylaşılıyor.
/// </summary>
[ApiController]
[Route("v1/webhooks/settlement")]
public sealed class SettlementWebhookController(
    WebhookSecrets secrets,
    IValidator<SettlementWebhookPayload> validator,
    TopupInboxWriter inbox,
    ILogger<SettlementWebhookController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 32 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sıra top-up ucuyla aynı: imza → parse → doğrulama → inbox → 202. Gerekçeler
    /// de aynı; <see cref="TopupWebhookController"/> yorumunda açık.
    /// </summary>
    [HttpPost("{provider}")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(string provider, CancellationToken ct)
    {
        if (!secrets.TryGet(provider, out var secret))
        {
            logger.LogWarning("Tanınmayan sağlayıcıdan settlement: {Provider}", provider);
            return Unauthorized();
        }

        var body = await ReadBodyAsync(ct);

        if (!WebhookSignature.IsValid(body, secret, Request.Headers[WebhookSignature.HeaderName]))
        {
            logger.LogWarning("Geçersiz settlement imzası. Sağlayıcı {Provider}", provider);
            return Unauthorized();
        }

        SettlementWebhookPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<SettlementWebhookPayload>(body, JsonOptions);
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
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var message = new SettlementReceived
        {
            Provider = provider,
            SettlementId = payload.SettlementId!,
            Currency = payload.Currency!.ToUpperInvariant(),
            GrossAmount = payload.GrossAmount!.Value,
            FeeAmount = payload.FeeAmount!.Value,
            NetAmount = payload.NetAmount!.Value,
            ProviderRefs = payload.References!,
            SettledAt = payload.SettledAt!.Value
        };

        var duplicate = await inbox.WriteAsync(message, Encoding.UTF8.GetString(body), ct);

        if (duplicate)
        {
            logger.LogInformation(
                "Tekrar eden settlement, inbox'a yazılmadı. {Provider}/{SettlementId}",
                provider, message.SettlementId);
        }

        return Accepted(new { accepted = true, duplicate });
    }

    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);

        return buffer.ToArray();
    }
}
