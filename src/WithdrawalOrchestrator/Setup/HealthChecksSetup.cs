using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
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
                timeout: TimeSpan.FromSeconds(3))
            // Broker olmadan çekim kabul edilmeye DEVAM eder: komut outbox'ta birikir
            // ve relay broker dönünce boşaltır. Outbox'ın varlık sebebi tam olarak bu,
            // dolayısıyla broker arızası bu servisi trafikten çektirmemeli (madde 26).
            .AddRabbitMqCheck("rabbitmq", HealthStatus.Degraded, HealthCheckEndpoints.ReadyTag);

        return services;
    }
}
