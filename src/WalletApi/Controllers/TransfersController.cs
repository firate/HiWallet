using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletApi.Responses;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

[ApiController]
[Route("v1/transfers")]
public sealed class TransfersController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Cüzdanlar arası transfer. Tek ACID transaction, saga yok (overview.md madde 4).
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU (decisions.md madde 4). Aynı key ile ikinci istek yeni transfer YAPMAZ,
    /// mevcut işlemi döner. Yanıt yine <c>201</c>; tekrar olduğu gövdedeki
    /// <c>replayed</c> ile bildirilir.
    ///
    /// Opsiyonel olsaydı şu senaryo sessizce çift harcama üretirdi: ledger commit
    /// oldu, yanıt dönerken bağlantı koptu, istemci "oldu mu olmadı mı" bilmediği
    /// için tekrar denedi. Anahtarsız tekrar hiçbir şeye takılmaz.
    /// </param>
    [HttpPost]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TransferResponse>> Create(
        [FromBody] CreateTransferRequest request,
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

        // Wolverine yalnızca in-process mediator olarak (decisions.md madde 1).
        var result = await bus.InvokeAsync<TransferResult>(
            request.ToCommand(idempotencyKey), ct);

        var response = TransferResponse.From(result);

        return CreatedAtAction(
            actionName: nameof(GetById),
            routeValues: new { transactionId = response.TransactionId },
            value: response);
    }

    /// <summary>
    /// Şimdilik yalnızca <c>CreatedAtAction</c>'ın Location header'ı için var.
    /// İşlem detayı endpoint'i henüz yazılmadı.
    /// </summary>
    [HttpGet("{transactionId:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult GetById(Guid transactionId)
    {
        return StatusCode(StatusCodes.Status501NotImplemented);
    }
}
