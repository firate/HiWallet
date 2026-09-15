using HiWallet.Bank.Fake.Application;
using HiWallet.Bank.Fake.Infrastructure.Callbacks;
using HiWallet.Bank.Fake.Infrastructure.Persistence;
using HiWallet.Shared.Infrastructure.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.Bank.Fake.Setup;

public static class BankFakeSetup
{
    /// <summary>
    /// Sahte bankanın KENDİ veritabanı. <c>hiwallet_bank</c> ile karıştırılmamalı —
    /// orası adaptörün, yani bizim (decisions.md madde 35).
    /// </summary>
    public const string ConnectionStringName = "BankFake";

    public static IServiceCollection AddBankFake(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BankFakeOptions>(configuration.GetSection(BankFakeOptions.SectionName));

        // Bağlantı dizesi KAYIT anında değil, context kurulurken okunuyor —
        // WebApplicationFactory konfigürasyonunu host kurulduktan sonra ekliyor.
        services.AddDbContextFactory<BankFakeDbContext>((provider, options) =>
            options.UseNpgsql(provider
                .GetRequiredService<IConfiguration>()
                .GetConnectionString(ConnectionStringName)));

        services.AddScoped<AcceptTransferHandler>();
        services.AddScoped<ScenarioStore>();
        services.AddScoped<TransferQueries>();

        // Callback göndericisi kendi HTTP istemcisini kullanıyor. Timeout kısa:
        // ulaşılamayan bir uca takılıp kalmak, sırada bekleyen callback'leri de
        // geciktirirdi.
        services.AddHttpClient(CallbackDispatcher.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(10));

        services.AddHostedService<CallbackDispatcher>();

        services.AddHealthChecks()
            // RabbitMQ kontrolü YOK: sahte banka broker'a hiç bağlanmıyor. Gerçek
            // banka da bağlanmazdı.
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3));

        return services;
    }

    /// <summary>Fail fast (baseline.md madde 1). Host KURULDUKTAN sonra çalışır.</summary>
    public static WebApplication ValidateBankFakeConfiguration(this WebApplication app)
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
