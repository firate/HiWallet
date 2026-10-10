using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BackofficeBff.Controllers;

/// <summary>
/// Askıdaki havalenin göndericiye iadesi; orchestrator'dan. Başlatmak <c>deposit.resolve</c>,
/// görmek <c>deposit.view</c> izni; orchestrator kontrol ediyor.
/// </summary>
[ApiController]
[Route("v1/deposit-returns")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class DepositReturnsController(WithdrawalOrchestratorClient orchestrator) : ControllerBase
{
    /// <summary>
    /// İadeyi başlatır; <c>202</c>: para henüz hareket etmedi. Havale aktarılmışsa ya da
    /// iadesi sürüyorsa iade <c>rejected</c>'da biter.
    /// </summary>
    /// <param name="idempotencyKey">ZORUNLU. Aynı anahtarla tekrar aynı iadeyi döner.</param>
    [HttpPost]
    [ProducesResponseType<DepositReturnAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] StartDepositReturnRequest request,
        [FromHeader(Name = InternalServiceClient.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        var response = await orchestrator.PostAsync<DepositReturnAcceptedResponse>(
            "v1/deposit-returns", request, idempotencyKey, ct);

        // Location yok: iç servisin adresi dışarı çıkmıyor.
        return Accepted(response);
    }

    /// <summary>İadenin son durumu.</summary>
    [HttpGet("{depositReturnId:guid}")]
    [ProducesResponseType<DepositReturnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<DepositReturnResponse> GetById(Guid depositReturnId, CancellationToken ct)
    {
        return await orchestrator.GetAsync<DepositReturnResponse>($"v1/deposit-returns/{depositReturnId}", ct);
    }
}
