using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.WalletService.Setup;

/// <summary>
/// Bu servisin readiness bağımlılıkları. Uçların kendisi Shared'da
/// (<see cref="HealthCheckEndpoints"/>); burada yalnızca "neye bakılacağı" var.
/// </summary>
public static class HealthChecksSetup
{
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
                tags: [HealthCheckEndpoints.ReadyTag],
                // Probe'un kendisi asılı kalmamalı; DB yavaşsa readiness hızlıca
                // "hazır değil" demeli, timeout'u orchestrator'a bırakmamalı.
                timeout: TimeSpan.FromSeconds(3))
            // Broker'sız top-up tüketilemez ama transfer çekirdeği çalışmaya devam
            // eder — o yol tek DB'de, ACID ve broker'a hiç dokunmuyor. Bu yüzden
            // Degraded: durum görünür olur, servis trafikten ÇEKİLMEZ. Unhealthy
            // olsaydı bir broker arızası çalışan transferleri de kapatırdı.
            .AddRabbitMqCheck("rabbitmq", HealthStatus.Degraded, HealthCheckEndpoints.ReadyTag);

        return services;
    }
}
