using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BackofficeBff.Controllers;

/// <summary>
/// Cüzdana geçirilemeyip askıya alınan havaleler. Görmek <c>deposit.view</c>, aktarmak
/// <c>deposit.resolve</c> izni; wallet-api kontrol ediyor.
/// </summary>
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

    /// <summary>
    /// Havaleyi hesabın varsayılan cüzdanına aktarır; ledger'da aktör çalışan. Hesap
    /// bireysel değilse, cüzdanı yoksa ya da seviyenin limiti yetmiyorsa <c>422</c>.
    /// </summary>
    /// <param name="idempotencyKey">ZORUNLU. Aynı anahtarla tekrar aynı cevabı alıyor.</param>
    [HttpPost("{suspendedDepositId:guid}/move")]
    [ProducesResponseType<SuspendedDepositMovedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SuspendedDepositMovedResponse> Move(
        Guid suspendedDepositId,
        [FromBody] MoveSuspendedDepositRequest request,
        [FromHeader(Name = InternalServiceClient.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        return await walletApi.PostAsync<SuspendedDepositMovedResponse>(
            $"v1/suspended-deposits/{suspendedDepositId}/move", request, idempotencyKey, ct);
    }
}
