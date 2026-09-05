using HiWallet.BankService.Application;
using HiWallet.BankService.Infrastructure.Messaging;
using HiWallet.BankService.Infrastructure.Persistence;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.BankService.Setup;

public static class BankServiceSetup
{
    /// <summary>Bu servisin kendi veritabanı. Wallet ve orchestrator şemalarına erişimi yok.</summary>
    public const string ConnectionStringName = "Bank";

    public static IServiceCollection AddBankService(this IServiceCollection services)
    {
        // Bağlantı dizesi KAYIT anında değil, context kurulurken okunuyor —
        // WebApplicationFactory konfigürasyonunu host kurulduktan sonra ekliyor.
        services.AddDbContextFactory<BankDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            options.UseNpgsql(configuration.GetConnectionString(ConnectionStringName));
        });

        services.AddScoped<StartBankTransferHandler>();
        services.AddScoped<ScenarioStore>();
        services.AddHostedService<BankCommandConsumer>();

        services.AddHealthChecks()
            // Bu servisin işi kuyruktan okuyup transferi kaydetmek; ikisinden biri
            // yoksa iş yapamıyor. wallet-consumer ile aynı gerekçe.
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3))
            .AddRabbitMqCheck("rabbitmq", HealthStatus.Unhealthy, HealthCheckEndpoints.ReadyTag);

        return services;
    }

    /// <summary>Fail fast (baseline.md madde 1). Host KURULDUKTAN sonra çalışır.</summary>
    public static WebApplication ValidateBankServiceConfiguration(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(ConnectionStringName)))
        {
            throw new InvalidOperationException(
                $"Zorunlu konfigürasyon eksik: ConnectionStrings:{ConnectionStringName}. " +
                "Local'de User Secrets, container'da .env üzerinden verilir.");
        }

        return app;
    }
}
