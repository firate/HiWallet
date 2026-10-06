using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Application.CardTopups;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

/// <summary>
/// Kartla yüklemenin limit payı. Kart yüklemesi servisi ödemeyi açmadan ÖNCE çağırıyor,
/// müşterinin token'ını iletiyor: seviye limitine sığmıyorsa <c>422</c> ve ödeme hiç
/// açılmıyor, kart çekilmiyor. Ödemenin kendisi ve sağlayıcı bu servisin işi değil; pay
/// ödeme kapandığında wallet-consumer'da kapanıyor.
///
/// Müşterinin ucu: çalışan müşteri yerine para yüklemiyor (varsayılan politika çalışanı
/// dışarıda bırakıyor).
/// </summary>
[ApiController]
[Route("v1/card-topup-holds")]
public sealed class CardTopupHoldsController(IMessageBus bus, AccountAccess access) : ControllerBase
{
    /// <summary>
    /// Payı ayırır. Aynı <c>holdId</c> ile ikinci istek yeni pay açmaz, mevcut payı
    /// <c>replayed: true</c> ile döner; limit yeniden sorulmaz.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CardTopupHoldResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Place([FromBody] PlaceCardTopupHoldRequest request, CancellationToken ct)
    {
        // Cüzdan çağıranın olmalı; başkasınınki yokmuş gibi 404.
        await access.EnsureWalletAsync(User.Subject(), request.WalletId, ct);

        var result = await bus.InvokeAsync<CardTopupHoldResult>(request.ToCommand(), ct);

        // Location YOK: payın okuma ucu yok; durumu kart yüklemesi servisi tutuyor.
        return StatusCode(StatusCodes.Status201Created, CardTopupHoldResponse.From(result));
    }
}
