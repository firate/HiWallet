using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletService.Application.Deposits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

/// <summary>
/// Cüzdana geçirilemeyip askıya alınan havaleler: açıklamada numara yok, gönderen hesabın
/// sahibi değil, limit aşıldı. Para bankamızda ve askı hesabında; kaynağa iade ya da bir
/// cüzdana geçirme kararı panelde. <c>deposit.view</c> izni.
/// </summary>
[ApiController]
[Route("v1/suspended-deposits")]
[Authorize(Policy = HiWalletPolicies.DepositView)]
public sealed class SuspendedDepositsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Askıdaki havaleler, yeniden eskiye.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [ProducesResponseType<SuspendedDepositsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<SuspendedDepositsResponse> List(
        CancellationToken ct,
        [FromQuery] Guid? after = null,
        [FromQuery] int size = SuspendedDepositPage.DefaultSize)
    {
        var page = await bus.InvokeAsync<SuspendedDepositPage>(new ListSuspendedDepositsQuery(after, size), ct);

        return SuspendedDepositsResponse.From(page);
    }
}
