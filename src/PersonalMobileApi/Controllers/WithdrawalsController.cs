using HiWallet.EdgeApi.InternalServices;
using HiWallet.PersonalMobileApi.Requests;
using HiWallet.PersonalMobileApi.Responses;
using HiWallet.PersonalMobileApi.Setup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalMobileApi.Controllers;

[ApiController]
[Route("v1/withdrawals")]
[EnableRateLimiting(RateLimitingSetup.CustomerPolicy)]
public sealed class WithdrawalsController(
    WalletApiClient walletApi,
    WithdrawalOrchestratorClient orchestrator) : ControllerBase
{
    /// <summary>
    /// IBAN'a para çekme başlatır. Cevap döndüğünde henüz hiçbir para hareket etmedi;
    /// sonuç <c>Location</c>'daki adresten izleniyor.
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU. Aynı anahtarla ikinci istek yeni çekim açmaz, mevcut olanı döner.
    /// </param>
    [HttpPost]
    [EnableRateLimiting(RateLimitingSetup.WithdrawalsPolicy)]
    [ProducesResponseType<WithdrawalAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWithdrawalRequest request,
        [FromHeader(Name = InternalServiceClient.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        // Çekimin idempotency kapsamı hesap. Hesap cüzdanın sahibi olarak wallet-api'den
        // okunuyor, istemcinin beyanından değil; cüzdan yoksa 404 buradan dönüyor.
        var wallet = await walletApi.GetAsync<WalletResponse>($"v1/wallets/{request.WalletId}", ct);

        var response = await orchestrator.PostAsync<WithdrawalAcceptedResponse>(
            "v1/withdrawals",
            new StartWithdrawal(wallet.AccountId, request.WalletId, request.Amount, request.Currency, request.DestinationIban),
            idempotencyKey,
            ct);

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

    /// <summary>withdrawal-orchestrator'ın beklediği gövde.</summary>
    private sealed record StartWithdrawal(
        Guid AccountId,
        Guid WalletId,
        decimal Amount,
        string Currency,
        string DestinationIban);
}
