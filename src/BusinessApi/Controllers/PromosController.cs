using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BusinessApi.Controllers;

[ApiController]
[Route("v1/promos")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class PromosController(WalletApiClient walletApi) : ControllerBase
{
    /// <summary>
    /// İşyeri kendi müşterisine promo veriyor (decisions.md madde 37). Promo işyerinin
    /// cash kovasından çıkıyor ve yalnızca bu işyerinde geçiyor. Fonlayan cüzdan
    /// entegrasyonun bağlı olduğu hesabın olmalı.
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU: para hareket ettiriyor. Aynı anahtarla ikinci istek yeni parti açmaz.
    /// </param>
    [HttpPost]
    [ProducesResponseType<PromoGrantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] GrantPromoRequest request,
        [FromHeader(Name = InternalServiceClient.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        var response = await walletApi.PostAsync<PromoGrantResponse>("v1/promos", request, idempotencyKey, ct);

        // Location YOK: parti müşterinin cüzdanının listesinde ve işyeri o listeyi
        // göremiyor.
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
