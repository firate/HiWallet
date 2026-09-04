using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.WalletService.Setup;

/// <summary>
/// Liveness ve readiness AYRI (baseline.md madde 4).
///
/// Ayrım orchestrator'ın davranışını belirliyor: liveness başarısızsa container
/// YENİDEN BAŞLATILIR, readiness başarısızsa yalnızca trafikten çekilir. DB geçici
/// olarak erişilemezse yeniden başlatmak durumu düzeltmez, sadece kötüleştirir —
/// o yüzden DB kontrolü readiness'ta, liveness'ta değil.
/// </summary>
public static class HealthChecksSetup
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddHiWalletHealthChecks(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(PersistenceSetup.ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                // Probe'un kendisi asılı kalmamalı; DB yavaşsa readiness hızlıca
                // "hazır değil" demeli, timeout'u orchestrator'a bırakmamalı.
                timeout: TimeSpan.FromSeconds(3));

        return services;
    }

    public static WebApplication MapHiWalletHealthChecks(this WebApplication app)
    {
        // Süreç ayakta mı? Hiçbir bağımlılık kontrol edilmiyor.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponse
        });

        // Trafik alabilir mi? Gerçek bağımlılıklar kontrol ediliyor.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponse
        });

        return app;
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
