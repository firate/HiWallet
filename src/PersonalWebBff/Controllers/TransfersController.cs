using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalWebBff.Controllers;

[ApiController]
[Route("v1/transfers")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class TransfersController(WalletApiClient walletApi) : ControllerBase
{
    /// <summary>Cüzdanlar arası transfer.</summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU. Aynı anahtarla ikinci istek yeni transfer yapmaz, mevcut işlemi
    /// <c>replayed: true</c> ile döner. Cevap alınamayan istek aynı anahtarla tekrar
    /// gönderilir.
    /// </param>
    [HttpPost]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransferRequest request,
        [FromHeader(Name = InternalServiceClient.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        var response = await walletApi.PostAsync<TransferResponse>("v1/transfers", request, idempotencyKey, ct);

        // Location YOK: işlemin detay ucu yok.
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
