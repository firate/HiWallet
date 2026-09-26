using HiWallet.EdgeApi.InternalServices;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace HiWallet.EdgeApi.Errors;

/// <summary>
/// İç servisin cevabını istemciye aktarır.
///
/// <b>İç servis cevap verdiyse</b> durum kodu ve gövde AYNEN geçiyor. Gövdedeki
/// <c>traceId</c> ön API'ninkiyle aynı: trace başlığı iç çağrıya taşınıyor, yani
/// destek talebindeki tek değer iki servisin log'unu da buluyor.
///
/// <b>Cevap alınamadıysa</b> (bağlantı yok, zaman aşımı, açık devre) <c>503</c>.
/// Bağlantı hatasının ayrıntısı yalnızca log'a gidiyor.
/// </summary>
internal sealed class InternalServiceExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<InternalServiceExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case InternalServiceException { Body.Length: > 0 } rejected:
                context.Response.StatusCode = (int)rejected.StatusCode;
                context.Response.ContentType = rejected.ContentType;
                await context.Response.Body.WriteAsync(rejected.Body, ct);
                return true;

            // Gövdesiz cevap: durum kodu korunuyor, gövdeyi ortak taban yazıyor.
            case InternalServiceException rejected:
                return await WriteProblemAsync(context, exception, (int)rejected.StatusCode, title: null);

            case HttpRequestException or TimeoutRejectedException or BrokenCircuitException:
                logger.LogWarning(exception, "İç servise ulaşılamadı: {Method} {Path}",
                    context.Request.Method, context.Request.Path);

                return await WriteProblemAsync(
                    context, exception, StatusCodes.Status503ServiceUnavailable,
                    title: "Servis geçici olarak kullanılamıyor");

            default:
                return false;
        }
    }

    private async ValueTask<bool> WriteProblemAsync(
        HttpContext context, Exception exception, int status, string? title)
    {
        context.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title },
            Exception = exception
        });
    }
}
