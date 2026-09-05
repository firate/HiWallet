using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.Shared.Infrastructure.HealthChecks;

/// <summary>
/// Liveness ve readiness AYRI (baseline.md madde 4).
///
/// Ayrım orchestrator'ın davranışını belirliyor: liveness başarısızsa container
/// YENİDEN BAŞLATILIR, readiness başarısızsa yalnızca trafikten çekilir. DB ya da
/// broker geçici olarak erişilemezse yeniden başlatmak durumu düzeltmez, sadece
/// kötüleştirir — o yüzden bağımlılık kontrolleri readiness'ta, liveness'ta değil.
///
/// Hangi kontrollerin kaydedileceği servise özel; uçların şekli değil. Kayıt her
/// serviste, uçlar burada.
/// </summary>
public static class HealthCheckEndpoints
{
    public const string ReadyTag = "ready";

    public static IEndpointRouteBuilder MapHiWalletHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Süreç ayakta mı? Hiçbir bağımlılık kontrol edilmiyor.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponse
        });

        // Trafik alabilir mi? Gerçek bağımlılıklar kontrol ediliyor.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponse
        });

        return endpoints;
    }

    private static async Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                // İstisna mesajı DEĞİL: bağlantı dizesi ve iç detay sızabilir.
                error = e.Value.Exception is null ? null : "check failed"
            })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
