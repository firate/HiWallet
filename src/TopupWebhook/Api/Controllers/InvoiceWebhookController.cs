using System.Text;
using System.Text.Json;
using FluentValidation;
using HiWallet.Shared.Contracts.Settlements;
using HiWallet.TopupWebhook.Api.Requests;
using HiWallet.TopupWebhook.Application;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.TopupWebhook.Api.Controllers;

/// <summary>
/// Sağlayıcının dönem sonu faturası (adım 5.6).
///
/// <b>Gövde sınırı diğer uçlardan büyük.</b> Fatura binlerce kalem referansı
/// taşıyabiliyor; 32 KB o listeyi kesip faturayı sessizce eksik kapsamlı yapardı.
/// İmza yine gövdenin tamamı üzerinde, yani sınır bir CPU tavanı olarak duruyor.
/// </summary>
[ApiController]
[Route("v1/webhooks/invoice")]
public sealed class InvoiceWebhookController(
    WebhookSecrets secrets,
    IValidator<InvoiceWebhookPayload> validator,
    TopupInboxWriter inbox,
    ILogger<InvoiceWebhookController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 512 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("{provider}")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(string provider, CancellationToken ct)
    {
        if (!secrets.TryGet(provider, out var secret))
        {
            logger.LogWarning("Tanınmayan sağlayıcıdan fatura: {Provider}", provider);
            return Unauthorized();
        }

        var body = await ReadBodyAsync(ct);

        if (!WebhookSignature.IsValid(body, secret, Request.Headers[WebhookSignature.HeaderName]))
        {
            logger.LogWarning("Geçersiz fatura imzası. Sağlayıcı {Provider}", provider);
            return Unauthorized();
        }

        InvoiceWebhookPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<InvoiceWebhookPayload>(body, JsonOptions);
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

        var message = new ProviderInvoiceReceived
        {
            Provider = provider,
            InvoiceRef = payload.InvoiceRef!,
            Currency = payload.Currency!.ToUpperInvariant(),
            Amount = payload.Amount!.Value,
            ProviderRefs = payload.References ?? [],
            IssuedAt = payload.IssuedAt!.Value
        };

        var duplicate = await inbox.WriteAsync(message, Encoding.UTF8.GetString(body), ct);

        if (duplicate)
        {
            logger.LogInformation(
                "Tekrar eden fatura, inbox'a yazılmadı. {Provider}/{InvoiceRef}",
                provider, message.InvoiceRef);
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
