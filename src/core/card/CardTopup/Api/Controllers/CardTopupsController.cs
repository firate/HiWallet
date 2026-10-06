using FluentValidation;
using HiWallet.CardTopup.Api.Requests;
using HiWallet.CardTopup.Api.Responses;
using HiWallet.CardTopup.Application;
using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.CardTopup.Api.Controllers;

[ApiController]
[Route("v1/card-topups")]
public sealed class CardTopupsController(
    StartCardTopupHandler handler,
    CardTopupQueries queries,
    IValidator<StartCardTopupRequest> validator) : ControllerBase
{
    /// <summary>
    /// Kartla yükleme başlatır: limit payı ayrılıyor ve sağlayıcıda ödeme açılıyor. Müşteri
    /// dönen <c>paymentUrl</c>'e gidip kartını giriyor; para ödeme kapandığında cüzdana
    /// geçiyor.
    ///
    /// Limit yetmiyorsa ödeme hiç açılmıyor: wallet'ın <c>422</c>'si aynen dönüyor. Müşterinin
    /// ucu; çalışan müşteri yerine para yüklemiyor (varsayılan politika).
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU. Aynı anahtarla ikinci istek yeni yükleme açmaz, mevcut olanı döner ve yarım
    /// kalmış adımı tamamlar.
    /// </param>
    [HttpPost]
    [ProducesResponseType<CardTopupAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Start(
        [FromBody] StartCardTopupRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["Idempotency-Key"] = ["Idempotency-Key başlığı zorunlu."]
                }));
        }

        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        var result = await handler.HandleAsync(request.ToCommand(idempotencyKey, User.Subject()), ct);
        var response = CardTopupAcceptedResponse.From(result);

        // Tekrar eden istek de 202; ayrım gövdedeki `replayed` alanında.
        return AcceptedAtAction(
            actionName: nameof(GetById),
            routeValues: new { cardTopupId = response.CardTopupId },
            value: response);
    }

    /// <summary>
    /// Yüklemenin son durumu; ödeme sayfasından dönen arayüz bunu yokluyor. Müşteri yalnızca
    /// kendi başlattığı yüklemeyi görüyor, başkasınınki yokmuş gibi <c>404</c>. Çalışan
    /// <c>customer.view</c> izniyle her yüklemeyi görüyor.
    /// </summary>
    [HttpGet("{cardTopupId:guid}")]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<CardTopupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CardTopupResponse>> GetById(Guid cardTopupId, CancellationToken ct)
    {
        var topup = await queries.FindAsync(cardTopupId, ct);

        if (topup is null || (!User.IsEmployee() && topup.Subject != User.Subject()))
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Kartla yükleme bulunamadı",
                type: "https://hiwallet.dev/problems/card-topup-not-found");
        }

        return CardTopupResponse.From(topup);
    }
}
