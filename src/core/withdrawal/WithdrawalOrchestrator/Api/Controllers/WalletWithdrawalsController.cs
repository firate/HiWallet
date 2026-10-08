using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WithdrawalOrchestrator.Api.Responses;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.WithdrawalOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/wallets/{walletId:guid}/withdrawals")]
public sealed class WalletWithdrawalsController(WithdrawalQueries queries) : ControllerBase
{
    /// <summary>
    /// Cüzdanın çekimleri, yeniden eskiye. Orchestrator hesabın kullanıcılarını bilmiyor:
    /// müşteri yalnızca kendi başlattığı çekimleri görüyor, başkasının cüzdanında liste boş.
    /// Çalışan <c>customer.view</c> izniyle cüzdanın bütün çekimlerini görüyor.
    /// </summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<WithdrawalsResponse>(StatusCodes.Status200OK)]
    public async Task<WithdrawalsResponse> List(
        Guid walletId,
        CancellationToken ct,
        [FromQuery] Guid? after = null,
        [FromQuery] int size = WithdrawalPage.DefaultSize)
    {
        var initiatedBy = User.IsEmployee() ? null : User.Subject();

        return WithdrawalsResponse.From(await queries.ListForWalletAsync(walletId, initiatedBy, after, size, ct));
    }
}
