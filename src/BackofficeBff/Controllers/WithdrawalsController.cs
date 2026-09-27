using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BackofficeBff.Controllers;

[ApiController]
[Route("v1/withdrawals")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class WithdrawalsController(WithdrawalOrchestratorClient orchestrator) : ControllerBase
{
    /// <summary>Müşterinin çekiminin son durumu. IBAN maskeli döner.</summary>
    [HttpGet("{withdrawalId:guid}")]
    [ProducesResponseType<WithdrawalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<WithdrawalResponse> GetById(Guid withdrawalId, CancellationToken ct)
    {
        return await orchestrator.GetAsync<WithdrawalResponse>($"v1/withdrawals/{withdrawalId}", ct);
    }
}
