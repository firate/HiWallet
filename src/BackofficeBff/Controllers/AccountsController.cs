using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BackofficeBff.Controllers;

[ApiController]
[Route("v1/accounts")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class AccountsController(WalletApiClient walletApi) : ControllerBase
{
    /// <summary>
    /// Müşterinin hesabı ve cüzdanları, bakiyeleriyle. Çalışan her hesabı görüyor;
    /// rolünü wallet-api de kontrol ediyor.
    /// </summary>
    [HttpGet("{accountId:guid}")]
    [ProducesResponseType<AccountDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AccountDetailResponse> GetById(Guid accountId, CancellationToken ct)
    {
        return await walletApi.GetAsync<AccountDetailResponse>($"v1/accounts/{accountId}", ct);
    }
}
