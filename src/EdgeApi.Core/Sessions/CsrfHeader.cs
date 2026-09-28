using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.EdgeApi.Sessions;

/// <summary>
/// BFF'in API istekleri bir başlık istiyor. Cookie'yi tarayıcı kendiliğinden ekliyor;
/// <c>SameSite=Lax</c> başka sitelerden gelen isteği kesiyor ama aynı sitenin başka bir
/// alt alan adından geleni kesmiyor. O sayfa bu başlığı ekleyemiyor: özel başlık CORS
/// ön kontrolünü tetikliyor ve BFF hiçbir kökene izin vermiyor.
/// </summary>
public static class CsrfHeader
{
    public const string Name = "X-CSRF";
    public const string Value = "1";

    public static IApplicationBuilder UseCsrfHeader(this IApplicationBuilder app, PathString apiPrefix) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(apiPrefix) && context.Request.Headers[Name] != Value)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = $"{Name} başlığı eksik.",
                        Detail = "Tarayıcıdaki uygulama her API isteğine bu başlığı ekliyor."
                    }
                });
                return;
            }

            await next();
        });
}
