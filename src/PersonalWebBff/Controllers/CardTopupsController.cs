using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalWebBff.Controllers;

[ApiController]
[Route("v1/card-topups")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class CardTopupsController(CardTopupClient cardTopup) : ControllerBase
{
    /// <summary>Ödeme sayfasından dönülen uygulama sayfası.</summary>
    private const string ReturnPath = "/kart-yukleme";

    /// <summary>
    /// Kartla yükleme başlatır. Müşteri dönen <c>paymentUrl</c>'e gidiyor; ödeme sayfası onu
    /// uygulamanın <c>/kart-yukleme</c> sayfasına yüklemenin kimliğiyle geri yolluyor.
    /// Limit yetmiyorsa <c>422</c> ve ödeme hiç açılmıyor.
    /// </summary>
    /// <param name="idempotencyKey">ZORUNLU. Aynı anahtarla ikinci istek yeni yükleme açmaz.</param>
    [HttpPost]
    [ProducesResponseType<CardTopupAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Start(
        [FromBody] StartCardTopupRequest request,
        [FromHeader(Name = InternalServiceClient.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        // Uygulamanın sayfaları bu host'ta; adres tarayıcının gördüğü adres (gateway arkasında
        // iletilen başlıklardan).
        var returnUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{ReturnPath}";

        var response = await cardTopup.PostAsync<CardTopupAcceptedResponse>(
            "v1/card-topups",
            new StartAppCardTopupRequest(request.WalletId, request.Amount, request.Currency, returnUrl),
            idempotencyKey,
            ct);

        return AcceptedAtAction(
            actionName: nameof(GetById),
            routeValues: new { cardTopupId = response.CardTopupId },
            value: response);
    }

    /// <summary>Yüklemenin son durumu; dönüş sayfası bunu yokluyor.</summary>
    [HttpGet("{cardTopupId:guid}")]
    [ProducesResponseType<CardTopupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<CardTopupResponse> GetById(Guid cardTopupId, CancellationToken ct)
    {
        return await cardTopup.GetAsync<CardTopupResponse>($"v1/card-topups/{cardTopupId}", ct);
    }
}
