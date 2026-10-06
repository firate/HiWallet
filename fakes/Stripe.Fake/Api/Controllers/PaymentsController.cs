using FluentValidation;
using HiWallet.Stripe.Fake.Payments;
using HiWallet.Stripe.Fake.Webhooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HiWallet.Stripe.Fake.Api.Controllers;

/// <summary>
/// Kart sağlayıcısının ödeme API'si: ödeme açmak ve durumunu sormak. Sonuç webhook'la
/// bildiriliyor; durum sorgusu bildirimi kaçıranın kontrol yolu.
/// </summary>
[ApiController]
[Route("v1/payments")]
public sealed class PaymentsController(
    PaymentStore payments,
    IValidator<OpenPaymentRequest> validator,
    IOptions<StripeFakeOptions> options,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Ödeme açar ve müşterinin kartını gireceği sayfanın adresini döner. Aynı referansla
    /// ikinci istek yeni ödeme açmaz, ilkini döner; farklı tutarla gelirse <c>409</c>.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Open([FromBody] OpenPaymentRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        var (payment, created) = payments.Open(
            request.Reference, request.Amount, request.Currency.ToUpperInvariant(), request.ReturnUrl, request.ExpiresAt);

        if (payment.Amount != request.Amount || payment.Currency != request.Currency.ToUpperInvariant())
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Bu referansla başka tutarda bir ödeme açılmış.");
        }

        var response = PaymentResponse.From(payment, options.Value, time.GetUtcNow());

        return created
            ? CreatedAtAction(nameof(GetById), new { paymentId = payment.Id }, response)
            : Ok(response);
    }

    [HttpGet("{paymentId}")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<PaymentResponse> GetById(string paymentId)
    {
        var payment = payments.Find(paymentId);

        return payment is null
            ? NotFound()
            : PaymentResponse.From(payment, options.Value, time.GetUtcNow());
    }
}

/// <param name="Reference">Ödemeyi açanın kimliği; webhook'ta geri geliyor.</param>
/// <param name="ExpiresAt">Oturumun kapandığı an; sonra ödeme kabul edilmiyor.</param>
public sealed record OpenPaymentRequest(
    Guid Reference, decimal Amount, string Currency, string ReturnUrl, DateTimeOffset ExpiresAt);

/// <param name="Status"><c>requires_payment</c>, <c>succeeded</c>, <c>canceled</c>, <c>expired</c>.</param>
/// <param name="PaymentUrl">Müşterinin tarayıcıda açacağı sayfa.</param>
public sealed record PaymentResponse(
    string Id, Guid Reference, string Status, decimal Amount, string Currency, DateTimeOffset ExpiresAt, string PaymentUrl)
{
    public static PaymentResponse From(CardPayment payment, StripeFakeOptions options, DateTimeOffset now) => new(
        payment.Id,
        payment.Reference,
        PaymentText.Of(payment.StatusAt(now)),
        payment.Amount,
        payment.Currency,
        payment.ExpiresAt,
        $"{options.PublicUrl!.TrimEnd('/')}/odeme/{payment.Id}");
}

public sealed class OpenPaymentRequestValidator : AbstractValidator<OpenPaymentRequest>
{
    public OpenPaymentRequestValidator(TimeProvider time)
    {
        RuleFor(r => r.Reference).NotEmpty().WithMessage("Referans zorunlu.");

        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("Tutar pozitif olmalı.");

        RuleFor(r => r.Currency).NotEmpty().Length(3).WithMessage("Para birimi üç harfli ISO kodu olmalı.");

        RuleFor(r => r.ReturnUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                         && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Dönüş adresi mutlak bir http(s) adresi olmalı.");

        RuleFor(r => r.ExpiresAt)
            .Must(expiresAt => expiresAt > time.GetUtcNow())
            .WithMessage("Oturumun bitişi gelecekte olmalı.");
    }
}
