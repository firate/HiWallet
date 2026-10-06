using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

[ApiController]
[Route("v1/transfers")]
public sealed class TransfersController(IMessageBus bus, AccountAccess access) : ControllerBase
{
    /// <summary>
    /// Cüzdanlar arası transfer. Tek ACID transaction, saga yok (overview.md madde 4).
    /// Alıcı cüzdanla ya da hesap numarasıyla; numara alıcının bu para birimindeki
    /// varsayılan cüzdanına çevriliyor, çekirdek yine cüzdandan cüzdana.
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU (decisions.md madde 4). Aynı key ile ikinci request yeni transfer YAPMAZ,
    /// mevcut işlemi döner. Response yine <c>201</c>; tekrar olduğu gövdedeki
    /// <c>replayed</c> ile bildirilir.
    ///
    /// Opsiyonel olsaydı şu senaryo sessizce çift harcama üretirdi: ledger commit
    /// oldu, response dönerken bağlantı koptu, istemci "oldu mu olmadı mı" bilmediği
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

        // Gönderen cüzdan çağıranın olmalı; alan cüzdan herkesin olabilir.
        await access.EnsureWalletAsync(User.Subject(), request.FromWalletId, ct);

        var toWalletId = request.ToWalletId
                         ?? await bus.InvokeAsync<Guid>(
                             new ResolveRecipientWalletQuery(AccountNumber.From(request.ToAccountNumber), request.Currency), ct);

        // Kendi numarasına, varsayılanı olan cüzdandan gönderen müşteri.
        if (toWalletId == request.FromWalletId)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.ToAccountNumber)] = ["Gönderen ve alan cüzdan aynı olamaz."]
            }));
        }

        // Wolverine yalnızca in-process mediator olarak (decisions.md madde 1).
        var result = await bus.InvokeAsync<TransferResult>(
            request.ToCommand(idempotencyKey, toWalletId), ct);

        // Location YOK: işlemin detay ucu yok. Olmayan bir adrese işaret eden Location,
        // istemciyi 404'e ya da 501'e gönderirdi.
        return StatusCode(StatusCodes.Status201Created, TransferResponse.From(result));
    }
}
