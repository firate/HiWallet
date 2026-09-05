using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Setup;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.TopupConsumer;

public static class TopupConsumerSetup
{
    public static IServiceCollection AddHiWalletTopupConsumer(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ProcessTopupHandler>();
        services.AddHostedService<TopupConsumerService>();

        services.AddHealthChecks()
            // Bu uygulamanın TEK işi kuyruktan okuyup ledger'a yazmak; ikisinden
            // biri yoksa iş yapamıyor. wallet-api'de broker Degraded'dı çünkü
            // transfer çekirdeği ona dokunmuyordu — burada öyle bir yol yok.
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(PersistenceSetup.ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3))
            .AddRabbitMqCheck("rabbitmq", HealthStatus.Unhealthy, HealthCheckEndpoints.ReadyTag);

        return services;
    }

    /// <summary>
    /// Fail fast (baseline.md madde 1). Host KURULDUKTAN sonra çalışır, kayıt
    /// anında değil — konfigürasyonun tüm kaynakları o noktada birleşmiş oluyor.
    /// Broker ayarları <c>AddHiWalletMessaging</c> içinde zaten
    /// <c>ValidateOnStart</c> ile doğrulanıyor.
    /// </summary>
    public static WebApplication ValidateTopupConsumerConfiguration(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(
                app.Configuration.GetConnectionString(PersistenceSetup.ConnectionStringName)))
        {
            throw new InvalidOperationException(
                $"Zorunlu konfigürasyon eksik: ConnectionStrings:{PersistenceSetup.ConnectionStringName}. " +
                "Local'de User Secrets, container'da .env üzerinden verilir.");
        }

        return app;
    }
}
