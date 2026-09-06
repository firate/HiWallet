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
    /// Cüzdanın güncel bakiyesi. Sistem hesapları (clearing, revenue) bu uçtan
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
}
