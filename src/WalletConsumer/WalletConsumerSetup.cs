using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletConsumer.Settlements;
using HiWallet.WalletConsumer.Topups;
using HiWallet.WalletConsumer.Withdrawals;
using HiWallet.WalletService.Application.Settlements;
using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Setup;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.WalletConsumer;

public static class WalletConsumerSetup
{
    public static IServiceCollection AddWalletConsumer(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Ledger'a asenkron giren iki kaynak. Ayrı kuyruklar, ayrı kanallar, ayrı
        // hosted service'ler — biri tıkanınca diğeri akmaya devam ediyor.
        services.AddScoped<ProcessTopupHandler>();
        services.AddHostedService<TopupConsumerService>();

        // Settlement: sağlayıcının batch ödemesi. Cevap yayınlamıyor, karşı taraf
        // yok — tek yönlü bildirim.
        services.AddScoped<ProcessSettlementHandler>();
        services.AddScoped<ProcessInvoiceHandler>();
        services.AddHostedService<SettlementConsumer>();

        services.AddScoped<DebitForWithdrawalHandler>();
        services.AddScoped<RefundWithdrawalHandler>();
        services.AddHostedService<WithdrawalCommandConsumer>();

        // Zamanlanmış işler burada, ayrı bir wallet-jobs deployable'ında DEĞİL:
        // ölçüt erişim seviyesi (decisions.md madde 28) ve job'ların da ingress'i
        // yok, aynı ledger'a aynı kütüphaneyle yazıyorlar. Bu uygulama
        // ölçeklendiğinde job'ın iki kez koşmasını engelleyen şey deployable
        // ayrımı değil, advisory lock (madde 3).
        services.AddWalletJobs(configuration);

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
    public static WebApplication ValidateWalletConsumerConfiguration(this WebApplication app)
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
