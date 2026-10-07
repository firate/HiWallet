using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BackofficeBff.Controllers;

/// <summary>Cüzdana geçirilemeyip askıya alınan havaleler. <c>deposit.view</c> izni; wallet-api kontrol ediyor.</summary>
[ApiController]
[Route("v1/suspended-deposits")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class SuspendedDepositsController(WalletApiClient walletApi) : ControllerBase
{
    /// <summary>Askıdaki havaleler, yeniden eskiye.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [ProducesResponseType<SuspendedDepositsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<SuspendedDepositsResponse> List([FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct)
    {
        return await walletApi.GetAsync<SuspendedDepositsResponse>(
            InternalServiceClient.Paged("v1/suspended-deposits", after?.ToString(), size), ct);
    }
}
