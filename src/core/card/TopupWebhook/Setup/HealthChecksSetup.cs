using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.TopupWebhook.Setup;

public static class HealthChecksSetup
{
    public static IServiceCollection AddTopupHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            // Postgres olmadan webhook KABUL EDİLEMEZ: inbox'a yazamazsak 200
            // dönemeyiz. Bu yüzden Unhealthy — servis trafikten çekilmeli.
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(PersistenceSetup.ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3))
            // Broker olmadan webhook kabul edilmeye DEVAM eder; inbox birikir ve
            // relay broker dönünce boşaltır. Inbox'ın varlık sebebi tam olarak bu,
            // dolayısıyla broker arızası bu servisi trafikten çektirmemeli.
            .AddRabbitMqCheck("rabbitmq", HealthStatus.Degraded, HealthCheckEndpoints.ReadyTag);

        return services;
    }
}
