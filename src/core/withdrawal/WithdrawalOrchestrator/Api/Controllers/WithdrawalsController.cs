using FluentValidation;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WithdrawalOrchestrator.Api.Requests;
using HiWallet.WithdrawalOrchestrator.Api.Responses;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.WithdrawalOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/withdrawals")]
public sealed class WithdrawalsController(
    StartWithdrawalHandler handler,
    ReviewWithdrawalHandler review,
    WithdrawalQueries queries,
    IValidator<CreateWithdrawalRequest> validator,
    IValidator<CancelWithdrawalRequest> cancelValidator) : ControllerBase
{
    /// <summary>
    /// Para çekme başlatır. Saga ile yürüyor (overview.md madde 6): bu response
    /// döndüğünde henüz hiçbir para hareket etmedi.
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU — transfer'dekinin aksine. Çekim çok adımlı ve dışarıya para
    /// çıkarıyor; anahtarsız bir tekrar gerçekten ikinci bir banka transferi
    /// başlatırdı. Aynı anahtarla ikinci request yeni çekim AÇMAZ, mevcut olanı döner.
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

        var result = await handler.HandleAsync(request.ToCommand(idempotencyKey, User.Subject()), ct);
        var response = WithdrawalAcceptedResponse.From(result);

        // 202, 201 değil: kaynak yaratıldı ama işin kendisi bitmedi. Tekrar eden
        // request de 202 — istemci için tekrar göndermek başarılı bir sonuçtur, ayrım
        // gövdedeki `replayed` alanında (decisions.md madde 29 ile aynı gerekçe).
        return AcceptedAtAction(
            actionName: nameof(GetById),
            routeValues: new { withdrawalId = response.WithdrawalId },
            value: response);
    }

    /// <summary>
    /// Çekimin son durumu. <c>POST</c> response'undaki <c>Location</c> buraya işaret ediyor.
    /// Müşteri yalnızca kendi başlattığı çekimi görüyor; başkasının çekimi yokmuş gibi
    /// <c>404</c>. Çalışan bir rolüyle her çekimi görüyor.
    /// </summary>
    [HttpGet("{withdrawalId:guid}")]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<WithdrawalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WithdrawalResponse>> GetById(
        Guid withdrawalId, CancellationToken ct)
    {
        var saga = await queries.FindAsync(withdrawalId, ct);

        if (saga is null || (!User.IsEmployee() && saga.InitiatedBySubject != User.Subject()))
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Çekim bulunamadı",
                type: "https://hiwallet.dev/problems/withdrawal-not-found");
        }

        return WithdrawalResponse.From(saga);
    }

    /// <summary>
    /// Bir durumdaki çekimler, en eski önce. İnceleme kuyruğu:
    /// <c>state=under_review</c>. Çalışanın ucu, her rol görüyor.
    /// </summary>
    /// <param name="state">Durumun metin adı (<c>under_review</c>, <c>bank_transfer_pending</c>, ...).</param>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [Authorize(Policy = HiWalletPolicies.CustomerView)]
    [ProducesResponseType<WithdrawalsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WithdrawalsResponse>> List(
        [FromQuery] string state,
        CancellationToken ct,
        [FromQuery] Guid? after = null,
        [FromQuery] int size = WithdrawalPage.DefaultSize)
    {
        WithdrawalState parsed;

        try
        {
            parsed = WithdrawalStates.FromText(state);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["state"] = [$"Bilinmeyen çekim durumu: {state}"] }));
        }

        return WithdrawalsResponse.From(await queries.ListAsync(parsed, after, size, ct));
    }

    /// <summary>
    /// İncelemedeki çekimi serbest bırakır: banka komutu gidiyor. <c>withdrawal.review</c> izni; kararı
    /// veren çalışan saga'ya yazılıyor.
    /// </summary>
    [HttpPost("{withdrawalId:guid}/release")]
    [Authorize(Policy = HiWalletPolicies.WithdrawalReview)]
    [ProducesResponseType<WithdrawalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WithdrawalResponse>> Release(Guid withdrawalId, CancellationToken ct)
    {
        return Decided(await review.ReleaseAsync(withdrawalId, User.Subject(), ct), StatusCodes.Status200OK);
    }

    /// <summary>
    /// İncelemedeki çekimi iptal eder: para cüzdana geri veriliyor ve çekim banka
    /// reddinden ayrı bir durumda (<c>cancelled</c>) bitiyor. <c>withdrawal.review</c> izni. <c>202</c>:
    /// ters kaydı wallet yazıyor, dönüldüğünde henüz yazılmadı.
    /// </summary>
    [HttpPost("{withdrawalId:guid}/cancel")]
    [Authorize(Policy = HiWalletPolicies.WithdrawalReview)]
    [ProducesResponseType<WithdrawalResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WithdrawalResponse>> Cancel(
        Guid withdrawalId, [FromBody] CancelWithdrawalRequest request, CancellationToken ct)
    {
        var validation = await cancelValidator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        return Decided(
            await review.CancelAsync(withdrawalId, User.Subject(), request.Reason.Trim(), ct),
            StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Karar yalnızca incelemedeki çekimde geçerli; başka durumda <c>422</c>: istek
    /// geçerli, kural izin vermiyor. Aynı anda verilen ikinci karar ise <c>409</c>
    /// (saga'nın version'ı).
    /// </summary>
    private ActionResult<WithdrawalResponse> Decided(ReviewOutcome outcome, int status)
    {
        if (outcome.Saga is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Çekim bulunamadı",
                type: "https://hiwallet.dev/problems/withdrawal-not-found");
        }

        if (outcome.Result is not TransitionResult.Applied)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Çekim incelemede değil",
                detail: $"Çekimin durumu {outcome.Saga.State.ToText()}; karar yalnızca incelemedeki çekimde verilir.",
                type: "https://hiwallet.dev/problems/withdrawal-not-under-review");
        }

        return StatusCode(status, WithdrawalResponse.From(outcome.Saga));
    }
}
