using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalMobileApi.Controllers;

[ApiController]
[Route("v1/card-topups")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class CardTopupsController(CardTopupClient cardTopup) : ControllerBase
{
    /// <summary>
    /// Kartla yükleme başlatır. Uygulama dönen <c>paymentUrl</c>'i açıyor; ödeme sayfası
    /// müşteriyi verilen <c>returnUrl</c>'e yüklemenin kimliğiyle (<c>cardTopupId</c>) geri
    /// yolluyor. Limit yetmiyorsa <c>422</c> ve ödeme hiç açılmıyor.
    /// </summary>
    /// <param name="idempotencyKey">ZORUNLU. Aynı anahtarla ikinci istek yeni yükleme açmaz.</param>
    [HttpPost]
    [ProducesResponseType<CardTopupAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Start(
        [FromBody] StartAppCardTopupRequest request,
        [FromHeader(Name = InternalServiceClient.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        var response = await cardTopup.PostAsync<CardTopupAcceptedResponse>("v1/card-topups", request, idempotencyKey, ct);

        return AcceptedAtAction(
            actionName: nameof(GetById),
            routeValues: new { cardTopupId = response.CardTopupId },
            value: response);
    }

    /// <summary>Yüklemenin son durumu.</summary>
    [HttpGet("{cardTopupId:guid}")]
    [ProducesResponseType<CardTopupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<CardTopupResponse> GetById(Guid cardTopupId, CancellationToken ct)
    {
        return await cardTopup.GetAsync<CardTopupResponse>($"v1/card-topups/{cardTopupId}", ct);
    }
}
