using HiWallet.Shared.Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.WithdrawalOrchestrator.Setup;

public static class HealthChecksSetup
{
    public static IServiceCollection AddOrchestratorHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            // Postgres olmadan saga BAŞLATILAMAZ ve İLERLETİLEMEZ: hem durum hem
            // gönderilecek komut aynı transaction'da oraya yazılıyor (madde 32).
            // Bu yüzden Unhealthy — servis trafikten çekilmeli.
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(PersistenceSetup.ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3));

        // Broker kontrolü henüz YOK: bu servis daha broker'a bağlanmıyor. Relay ve
        // event tüketicisi geldiğinde eklenecek ve Degraded olacak — outbox'ın varlık
        // sebebi tam olarak broker yokken de istek kabul edebilmek (madde 26).

        return services;
    }
}
