using HiWallet.WalletApi.Requests;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletService.Application.Promos;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

[ApiController]
[Route("v1/promos")]
public sealed class PromosController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// İşyerinin kendi müşterisine promo vermesi (decisions.md madde 37). İşyerinin
    /// cash kovası düşüyor, müşterinin promo kovası artıyor; parti yalnızca işyerinin
    /// kendisinde geçerli.
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU (decisions.md madde 4): para hareket ettiriyor. Aynı anahtarla ikinci
    /// request yeni parti açmaz, mevcut partiyi <c>replayed: true</c> ile döner.
    /// </param>
    [HttpPost]
    [ProducesResponseType<PromoGrantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PromoGrantResponse>> Create(
        [FromBody] GrantPromoRequest request,
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

        var result = await bus.InvokeAsync<GrantPromoResult>(request.ToCommand(idempotencyKey), ct);

        // Parti, alan cüzdanın parti listesinde görünüyor.
        return CreatedAtAction(
            actionName: nameof(WalletsController.GetPromos),
            controllerName: "Wallets",
            routeValues: new { walletId = request.WalletId },
            value: PromoGrantResponse.From(result));
    }
}
