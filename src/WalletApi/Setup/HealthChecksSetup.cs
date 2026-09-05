using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.WalletService.Setup;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.WalletApi.Setup;

/// <summary>
/// Bu servisin readiness bağımlılıkları. Uçların kendisi Shared'da
/// (<see cref="HealthCheckEndpoints"/>); burada yalnızca "neye bakılacağı" var.
///
/// Listede TEK bir şey var ve bu bilinçli: top-up tüketicisi ayrı bir deployable'a
/// taşındıktan sonra bu uygulamanın broker ile hiç işi kalmadı (decisions.md
/// madde 28). Önceden burada Degraded dönen bir RabbitMQ kontrolü vardı;
/// bağımlılık ortadan kalkınca kontrol de kalktı.
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
                timeout: TimeSpan.FromSeconds(3));

        return services;
    }
}
