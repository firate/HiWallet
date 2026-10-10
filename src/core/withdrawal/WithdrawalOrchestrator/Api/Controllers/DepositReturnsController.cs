using FluentValidation;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WithdrawalOrchestrator.Api.Requests;
using HiWallet.WithdrawalOrchestrator.Api.Responses;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using HiWallet.WithdrawalOrchestrator.Application.DepositReturns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WithdrawalOrchestrator.Api.Controllers;

/// <summary>
/// Askıdaki havalenin göndericiye iadesi; çalışana. Başlatmak <c>deposit.resolve</c>,
/// görmek <c>deposit.view</c> izni. Havaleyi wallet, göndericinin IBAN'ını banka
/// entegrasyonu biliyor; burada yalnızca havalenin kimliği var.
/// </summary>
[ApiController]
[Route("v1/deposit-returns")]
public sealed class DepositReturnsController(
    StartDepositReturnHandler handler,
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    IValidator<StartDepositReturnRequest> validator) : ControllerBase
{
    /// <summary>
    /// İadeyi başlatır. Dönüldüğünde para hareket etmedi: havale askıdan düşülecek, sonra
    /// banka gönderecek. Havale aktarılmışsa ya da iadesi sürüyorsa iade <c>rejected</c>'da
    /// biter. Aynı anahtarla tekrar aynı iadeyi döner.
    /// </summary>
    /// <param name="idempotencyKey">ZORUNLU: iade dışarıya para çıkarıyor.</param>
    [HttpPost]
    [Authorize(Policy = HiWalletPolicies.DepositResolve)]
    [ProducesResponseType<DepositReturnAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] StartDepositReturnRequest request,
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

        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        var response = DepositReturnAcceptedResponse.From(
            await handler.HandleAsync(request.ToCommand(idempotencyKey, User.Subject()), ct));

        return AcceptedAtAction(
            actionName: nameof(GetById),
            routeValues: new { depositReturnId = response.DepositReturnId },
            value: response);
    }

    /// <summary>İadenin son durumu.</summary>
    [HttpGet("{depositReturnId:guid}")]
    [Authorize(Policy = HiWalletPolicies.DepositView)]
    [ProducesResponseType<DepositReturnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid depositReturnId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var saga = await db.DepositReturns.AsNoTracking().FirstOrDefaultAsync(s => s.Id == depositReturnId, ct);

        return saga is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "İade bulunamadı")
            : Ok(DepositReturnResponse.From(saga));
    }
}
