using HiWallet.Onboarding.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Onboarding.Setup;

/// <summary>
/// İş kuralı reddi <c>422</c>, çakışma <c>409</c>, bulunamayan <c>404</c>. Gövdedeki
/// <c>rule</c> uygulamanın müşteriye ne göstereceğini seçtiği ad. Bağlı bir servise
/// (kimlik sağlayıcı, wallet, SMS, nüfus kaydı) ulaşılamazsa <c>503</c>: müşteri
/// adımı yeniden deneyebilir.
/// </summary>
internal sealed class OnboardingExceptionHandler(IProblemDetailsService problemDetails, ILogger<OnboardingExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, rule) = exception switch
        {
            OnboardingRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Message, e.Rule),
            OnboardingConflictException e => (StatusCodes.Status409Conflict, e.Message, e.Rule),
            OnboardingNotFoundException e => (StatusCodes.Status404NotFound, e.Message, (string?)null),
            HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "Bağlı bir servis şu an yanıt vermiyor; tekrar dene.", null),
            _ => (0, string.Empty, null)
        };

        if (status == 0)
        {
            return false;
        }

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            logger.LogWarning(exception, "Bağlı bir servise ulaşılamadı.");
        }

        var problem = new ProblemDetails { Status = status, Title = title };

        if (rule is not null)
        {
            problem.Extensions["rule"] = rule;
        }

        context.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
