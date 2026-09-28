using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.WalletService.Domain.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletApi.Setup;

/// <summary>
/// wallet-api'nin domain hataları, RFC 7807 (baseline.md madde 5). Yakalanmamış
/// istisnanın 500'e çevrilmesi ortak tabanda (Shared.Infrastructure); burası onun
/// üstüne yalnızca iş kuralı ayrımlarını ekliyor.
///
/// Ayrım kritik: <c>422</c> iş kuralı reddi (request geçerliydi, kural izin vermedi),
/// <c>409</c> concurrency çakışması (kural sorunu yok, sistem yarıştı — retry mantıklı).
/// İkisi karıştırılmaz (CLAUDE.md "API").
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddWalletProblemDetails(this IServiceCollection services)
    {
        services.AddHiWalletProblemDetails();

        // Sıra önemli: ilk eşleşen kazanır, en özelden genele. Hiçbiri eşleşmezse
        // ortak taban devreye giriyor ve detaysız 500 yazıyor.
        services.AddExceptionHandler<InvalidDefinitionExceptionHandler>();
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
            TransferTypeMismatchException => "transfer_type_mismatch",
            LimitExceededException limit => limit.LimitName,
            IncomingLimitExceededException incoming => incoming.LimitName,
            KycLevelNotApplicableException => "kyc_level_not_applicable",
            UnsupportedCurrencyException => "unsupported_currency",
            PromoGrantRejectedException => "promo_grant_rejected",
            AccountRuleException => "account_rule",
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

/// <summary>
/// Tutarsız tanım → <c>400</c>: kuralın parçaları birbirini tutmuyor. Kuralları domain
/// fabrikası tutuyor, mesaj oradan geliyor.
/// </summary>
internal sealed class InvalidDefinitionExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not InvalidDefinitionException invalid)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Tanım tutarsız",
                Detail = invalid.Message,
                Type = "https://hiwallet.dev/problems/invalid-definition"
            },
            Exception = exception
        });
    }
}

/// <summary>
/// Cüzdan ya da hesap yok → <c>404</c>. Tip listesi yerine ortak taban yakalanıyor:
/// yeni bir "bulunamadı" türü eklendiğinde listeye eklemeyi unutmak <c>500</c> üretirdi.
/// </summary>
internal sealed class NotFoundExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not NotFoundException notFound)
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
                Title = notFound switch
                {
                    AccountNotFoundException => "Hesap bulunamadı",
                    PromoCampaignNotFoundException => "Kampanya bulunamadı",
                    _ => "Cüzdan bulunamadı"
                },
                Detail = notFound.Message,
                Type = notFound switch
                {
                    AccountNotFoundException => "https://hiwallet.dev/problems/account-not-found",
                    PromoCampaignNotFoundException => "https://hiwallet.dev/problems/campaign-not-found",
                    _ => "https://hiwallet.dev/problems/wallet-not-found"
                }
            },
            Exception = exception
        });
    }
}

/// <summary>
/// Optimistic lock çakışması, retry'lar tükendi → <c>409</c> (decisions.md madde 9).
/// İstemci aynı request'i tekrar gönderebilir; bu yüzden 422'den ayrı.
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
