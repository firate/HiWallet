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

    /// <summary>
    /// Bir durumdaki çekimler, en eski önce. İnceleme kuyruğu: <c>state=under_review</c>.
    /// </summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [ProducesResponseType<WithdrawalsResponse>(StatusCodes.Status200OK)]
    public async Task<WithdrawalsResponse> List(
        [FromQuery] string state, [FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct)
    {
        var path = InternalServiceClient.Paged("v1/withdrawals", after?.ToString(), size);
        var separator = path.Contains('?') ? '&' : '?';

        return await orchestrator.GetAsync<WithdrawalsResponse>(
            $"{path}{separator}state={Uri.EscapeDataString(state)}", ct);
    }

    /// <summary>İncelemedeki çekimi serbest bırakır: banka komutu gidiyor. Operasyon rolü.</summary>
    [HttpPost("{withdrawalId:guid}/release")]
    [ProducesResponseType<WithdrawalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<WithdrawalResponse> Release(Guid withdrawalId, CancellationToken ct)
    {
        return await orchestrator.PostAsync<WithdrawalResponse>(
            $"v1/withdrawals/{withdrawalId}/release", body: null, idempotencyKey: null, ct);
    }

    /// <summary>
    /// İncelemedeki çekimi iptal eder: para cüzdana geri veriliyor. Operasyon rolü.
    /// <c>202</c>: ters kaydı wallet yazıyor.
    /// </summary>
    [HttpPost("{withdrawalId:guid}/cancel")]
    [ProducesResponseType<WithdrawalResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(
        Guid withdrawalId, [FromBody] CancelWithdrawalRequest request, CancellationToken ct)
    {
        var response = await orchestrator.PostAsync<WithdrawalResponse>(
            $"v1/withdrawals/{withdrawalId}/cancel", request, idempotencyKey: null, ct);

        return Accepted(response);
    }
}
