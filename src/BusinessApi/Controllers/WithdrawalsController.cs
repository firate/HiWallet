using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BusinessApi.Controllers;

[ApiController]
[Route("v1/withdrawals")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class WithdrawalsController(
    WalletApiClient walletApi,
    WithdrawalOrchestratorClient orchestrator) : ControllerBase
{
    /// <summary>
    /// İşyerinin kasasından IBAN'a çekim. Cevap döndüğünde henüz hiçbir para hareket
    /// etmedi; sonuç <c>Location</c>'daki adresten izleniyor.
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU. Aynı anahtarla ikinci istek yeni çekim açmaz, mevcut olanı döner.
    /// </param>
    [HttpPost]
    [EnableRateLimiting(EdgeRateLimiting.WithdrawalsPolicy)]
    [ProducesResponseType<WithdrawalAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWithdrawalRequest request,
        [FromHeader(Name = InternalServiceClient.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        var response = await orchestrator.StartAsync(walletApi, request, idempotencyKey, ct);

        return AcceptedAtAction(
            actionName: nameof(GetById),
            routeValues: new { withdrawalId = response.WithdrawalId },
            value: response);
    }

    /// <summary>Çekimin son durumu. IBAN maskeli döner.</summary>
    [HttpGet("{withdrawalId:guid}")]
    [ProducesResponseType<WithdrawalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<WithdrawalResponse> GetById(Guid withdrawalId, CancellationToken ct)
    {
        return await orchestrator.GetAsync<WithdrawalResponse>($"v1/withdrawals/{withdrawalId}", ct);
    }
}
