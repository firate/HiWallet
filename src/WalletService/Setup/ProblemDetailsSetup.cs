using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Setup;

/// <summary>
/// Global hata yönetimi, RFC 7807 (baseline.md madde 5).
///
/// Ayrım kritik: <c>422</c> iş kuralı reddi (istek geçerliydi, kural izin vermedi),
/// <c>409</c> concurrency çakışması (kural sorunu yok, sistem yarıştı — retry mantıklı).
/// İkisi karıştırılmaz (CLAUDE.md "API").
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddHiWalletProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // Log ile yanıtı eşleştirebilmek için; OTel trace_id'siyle aynı değer.
                context.ProblemDetails.Extensions["traceId"] =
                    System.Diagnostics.Activity.Current?.TraceId.ToString()
                    ?? context.HttpContext.TraceIdentifier;
            });

        // Sıra önemli: ilk eşleşen kazanır, en özelden genele.
        services.AddExceptionHandler<DomainExceptionHandler>();
        services.AddExceptionHandler<NotFoundExceptionHandler>();
        services.AddExceptionHandler<ConcurrencyExceptionHandler>();

        return services;
    }
}

/// <summary>Yetersiz bakiye, limit aşımı → <c>422</c>. Hata değil, iş kuralı reddi.</summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "İşlem iş kuralı gereği reddedildi",
            Detail = domain.Message,
            Type = "https://hiwallet.dev/problems/business-rule"
        };

        problem.Extensions["rule"] = domain switch
        {
            InsufficientFundsException => "insufficient_funds",
            LimitExceededException limit => limit.LimitName,
            _ => "business_rule"
        };

        context.Response.StatusCode = problem.Status.Value;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}

/// <summary>Cüzdan yok → <c>404</c>.</summary>
internal sealed class NotFoundExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not WalletNotFoundException notFound)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Cüzdan bulunamadı",
                Detail = notFound.Message,
                Type = "https://hiwallet.dev/problems/wallet-not-found"
            },
            Exception = exception
        });
    }
}

/// <summary>
/// Optimistic lock çakışması, retry'lar tükendi → <c>409</c> (decisions.md madde 9).
/// İstemci aynı isteği tekrar gönderebilir; bu yüzden 422'den ayrı.
/// </summary>
internal sealed class ConcurrencyExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status409Conflict;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "İşlem çakıştı",
                // İç detay sızdırılmıyor: hangi satırın hangi version'da olduğu
                // client'ı ilgilendirmiyor.
                Detail = "Aynı cüzdan üzerinde eşzamanlı işlem var. Tekrar deneyin.",
                Type = "https://hiwallet.dev/problems/concurrency-conflict"
            },
            Exception = exception
        });
    }
}
