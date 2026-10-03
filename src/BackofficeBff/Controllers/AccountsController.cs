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

    /// <summary>İşyerinin platform fonlu promo kabulü. Pazarlama rolü; wallet-api kontrol ediyor.</summary>
    [HttpPut("{accountId:guid}/accepts-promo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetAcceptsPromo(
        Guid accountId, [FromBody] SetAcceptsPromoRequest request, CancellationToken ct)
    {
        await walletApi.PutAsync($"v1/accounts/{accountId}/accepts-promo", request, ct);

        return NoContent();
    }
}
