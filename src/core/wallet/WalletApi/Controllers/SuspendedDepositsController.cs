using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

/// <summary>
/// Cüzdana geçirilemeyip askıya alınan havaleler: açıklamada numara yok, gönderen hesabın
/// sahibi değil, limit aşıldı. Para bankamızda ve askı hesabında; bir cüzdana aktarma
/// kararı panelde. Görmek <c>deposit.view</c>, aktarmak <c>deposit.resolve</c> izni.
/// </summary>
[ApiController]
[Route("v1/suspended-deposits")]
public sealed class SuspendedDepositsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Askıdaki havaleler, yeniden eskiye; karar verilmiş olanlar listede yok.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [Authorize(Policy = HiWalletPolicies.DepositView)]
    [ProducesResponseType<SuspendedDepositsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<SuspendedDepositsResponse> List(
        CancellationToken ct,
        [FromQuery] Guid? after = null,
        [FromQuery] int size = SuspendedDepositPage.DefaultSize)
    {
        var page = await bus.InvokeAsync<SuspendedDepositPage>(new ListSuspendedDepositsQuery(after, size), ct);

        return SuspendedDepositsResponse.From(page);
    }

    /// <summary>
    /// Havaleyi hesabın varsayılan cüzdanına aktarır; ledger'da yükleme, aktör çalışan. Hesap
    /// bireysel olmalı, bu para biriminde varsayılan cüzdanı olmalı ve seviyenin limiti
    /// yetmeli; değilse <c>422</c> ve para askıda kalıyor. Havale bir kez aktarılıyor: aynı
    /// anahtarla tekrar aynı cevabı alıyor, başka bir karar <c>422</c>.
    /// </summary>
    [HttpPost("{suspendedDepositId:guid}/move")]
    [Authorize(Policy = HiWalletPolicies.DepositResolve)]
    [ProducesResponseType<SuspendedDepositMovedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SuspendedDepositMovedResponse>> Move(
        Guid suspendedDepositId,
        [FromBody] MoveSuspendedDepositRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["Idempotency-Key"] = ["Idempotency-Key başlığı zorunlu."]
                }));
        }

        var result = await bus.InvokeAsync<MoveSuspendedDepositResult>(
            new MoveSuspendedDepositCommand(
                suspendedDepositId, AccountNumber.From(request.AccountNumber), User.Subject(), idempotencyKey),
            ct);

        return SuspendedDepositMovedResponse.From(result);
    }
}
