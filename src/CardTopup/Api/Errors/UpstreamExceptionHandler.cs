using HiWallet.CardTopup.Infrastructure.Upstream;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.CardTopup.Api.Errors;

/// <summary>
/// Çağrılan servisin cevabını istemciye aktarır.
/// <list type="bullet">
/// <item><b>wallet-api reddettiyse</b> durum kodu ve gövde AYNEN geçiyor: limitin kararı
/// wallet'ta, bu servis onu yeniden yorumlamıyor. Arayüz <c>rule</c> alanından mesaj seçiyor.</item>
/// <item><b>Cevap alınamadıysa</b> <c>503</c>. Yükleme kaydı duruyor; aynı anahtarla tekrar
/// eden istek kaldığı yerden devam ediyor.</item>
/// </list>
/// </summary>
internal sealed class UpstreamExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<UpstreamExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case WalletRejectedException { Body.Length: > 0 } rejected:
                context.Response.StatusCode = (int)rejected.StatusCode;
                context.Response.ContentType = rejected.ContentType;
                await context.Response.Body.WriteAsync(rejected.Body, ct);
                return true;

            case WalletRejectedException rejected:
                return await WriteProblemAsync(context, exception, (int)rejected.StatusCode, title: null);

            case UpstreamUnavailableException:
                logger.LogWarning(exception, "Çağrılan servis cevap vermedi: {Method} {Path}",
                    context.Request.Method, context.Request.Path);

                return await WriteProblemAsync(
                    context, exception, StatusCodes.Status503ServiceUnavailable,
                    title: "Servis geçici olarak kullanılamıyor");

            default:
                return false;
        }
    }

    private async ValueTask<bool> WriteProblemAsync(HttpContext context, Exception exception, int status, string? title)
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
