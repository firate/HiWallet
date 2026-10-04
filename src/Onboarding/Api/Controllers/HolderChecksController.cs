using HiWallet.Onboarding.Application;
using HiWallet.Onboarding.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Onboarding.Api.Controllers;

/// <summary>
/// Havalenin göndereni hesabın sahibi mi. Yalnızca wallet-consumer soruyor
/// (<see cref="WalletConsumerAccess"/>); kimlik numarası gövdede, adreste DEĞİL: adres
/// erişim log'larına düşüyor.
/// </summary>
[ApiController]
[Route("v1/holder-checks")]
[Authorize(Policy = WalletConsumerAccess.Policy)]
public sealed class HolderChecksController(HolderCheckService holders) : ControllerBase
{
    /// <summary>Kimlik numarası sahibin doğrulanmış numarası mı. Cevap yalnızca evet ya da hayır.</summary>
    [HttpPost]
    [ProducesResponseType<HolderCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<HolderCheckResponse> Check([FromBody] HolderCheckRequest request, CancellationToken ct) =>
        new(await holders.IsHolderAsync(request.Holder, request.NationalId, ct));
}
