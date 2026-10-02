using HiWallet.StaffAdmin.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.StaffAdmin.Setup;

/// <summary>
/// İş kuralı reddi <c>422</c>, çakışma <c>409</c>, bulunamayan <c>404</c>. Gövdedeki
/// <c>rule</c> panelin ne göstereceğini seçtiği ad. Kimlik sağlayıcıya ulaşılamazsa
/// <c>503</c>: yönetici işi yeniden deneyebilir.
/// </summary>
internal sealed class StaffAdminExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<StaffAdminExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, rule) = exception switch
        {
            StaffAdminRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Message, e.Rule),
            StaffAdminConflictException e => (StatusCodes.Status409Conflict, e.Message, e.Rule),
            StaffAdminNotFoundException e => (StatusCodes.Status404NotFound, e.Message, (string?)null),
            HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "Kimlik sağlayıcı şu an yanıt vermiyor; tekrar dene.", null),
            _ => (0, string.Empty, null)
        };

        if (status == 0)
        {
            return false;
        }

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            logger.LogWarning(exception, "Kimlik sağlayıcıya ulaşılamadı.");
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
