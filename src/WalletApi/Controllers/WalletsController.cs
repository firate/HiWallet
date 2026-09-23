using HiWallet.WalletApi.Responses;
using HiWallet.WalletService.Application.Balances;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

[ApiController]
[Route("v1/wallets")]
public sealed class WalletsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Cüzdanın güncel bakiyesi. Sistem hesapları (clearing, revenue) bu endpoint'ten
    /// GÖRÜNMEZ — onlar iç muhasebe, public API'nin cevaplayacağı soru değil.
    /// </summary>
    [HttpGet("{walletId:guid}")]
    [ProducesResponseType<WalletResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletResponse>> GetById(Guid walletId, CancellationToken ct)
    {
        var view = await bus.InvokeAsync<WalletView>(new GetWalletQuery(walletId), ct);

        return Ok(WalletResponse.From(view));
    }

    /// <summary>
    /// Cüzdanın hareketleri, yeniden eskiye. Sayfalama CURSOR ile
    /// (<c>baseline.md</c> madde 8): ledger append-only ve yeni satırlar listenin
    /// başına giriyor, offset kullanılsaydı iki sayfa arasında gelen bir hareket
    /// sayfayı kaydırır ve müşteri aynı kaydı iki kez görürdü.
    /// </summary>
    /// <param name="after">
    /// Önceki sayfanın son hareketinin kimliği. İlk sayfada verilmiyor.
    /// </param>
    /// <param name="size">
    /// Sayfa boyutu. Tavanın üstü reddedilmiyor, tavana çekiliyor — istemciyi
    /// kırmadan sunucuyu koruyor.
    /// </param>
    [HttpGet("{walletId:guid}/movements")]
    [ProducesResponseType<WalletMovementsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletMovementsResponse>> GetMovements(
        Guid walletId,
        CancellationToken ct,
        [FromQuery] long? after = null,
        [FromQuery] int size = WalletMovementPage.DefaultSize)
    {
        var page = await bus.InvokeAsync<WalletMovementPage>(
            new GetWalletMovementsQuery(walletId, after, size), ct);

        return Ok(WalletMovementsResponse.From(page));
    }
}
