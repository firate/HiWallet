using FluentValidation;
using HiWallet.WithdrawalOrchestrator.Api.Requests;
using HiWallet.WithdrawalOrchestrator.Api.Responses;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.WithdrawalOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/withdrawals")]
public sealed class WithdrawalsController(
    StartWithdrawalHandler handler,
    WithdrawalQueries queries,
    IValidator<CreateWithdrawalRequest> validator) : ControllerBase
{
    /// <summary>
    /// Para çekme başlatır. Saga ile yürüyor (overview.md madde 6): bu yanıt
    /// döndüğünde henüz hiçbir para hareket etmedi.
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU — transfer'dekinin aksine. Çekim çok adımlı ve dışarıya para
    /// çıkarıyor; anahtarsız bir tekrar gerçekten ikinci bir banka transferi
    /// başlatırdı. Aynı anahtarla ikinci istek yeni çekim AÇMAZ, mevcut olanı döner.
    /// </param>
    [HttpPost]
    [ProducesResponseType<WithdrawalAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWithdrawalRequest request,
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

        var result = await handler.HandleAsync(request.ToCommand(idempotencyKey), ct);
        var response = WithdrawalAcceptedResponse.From(result);

        // 202, 201 değil: kaynak yaratıldı ama işin kendisi bitmedi. Tekrar eden
        // istek de 202 — istemci için tekrar göndermek başarılı bir sonuçtur, ayrım
        // gövdedeki `replayed` alanında (decisions.md madde 29 ile aynı gerekçe).
        return AcceptedAtAction(
            actionName: nameof(GetById),
            routeValues: new { withdrawalId = response.WithdrawalId },
            value: response);
    }

    /// <summary>Çekimin son durumu. <c>POST</c> yanıtındaki <c>Location</c> buraya işaret ediyor.</summary>
    [HttpGet("{withdrawalId:guid}")]
    [ProducesResponseType<WithdrawalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WithdrawalResponse>> GetById(
        Guid withdrawalId, CancellationToken ct)
    {
        var saga = await queries.FindAsync(withdrawalId, ct);

        if (saga is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Çekim bulunamadı",
                type: "https://hiwallet.dev/problems/withdrawal-not-found");
        }

        return WithdrawalResponse.From(saga);
    }
}
