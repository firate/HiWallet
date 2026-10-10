using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletConsumer.CardTopups;
using HiWallet.WalletConsumer.Deposits;
using HiWallet.WalletConsumer.Identity;
using HiWallet.WalletConsumer.Settlements;
using HiWallet.WalletConsumer.Withdrawals;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.CardTopups;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Application.Settlements;
using HiWallet.WalletService.Application.DepositReturns;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Setup;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HiWallet.WalletConsumer;

public static class WalletConsumerSetup
{
    private const string OnboardingUrlKey = "InternalServices:Onboarding:BaseUrl";

    private const string KeycloakTokenClient = "keycloak-token";

    public static IServiceCollection AddWalletConsumer(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Ledger'a asenkron giren her kaynak ayrı kuyruk, ayrı kanal, ayrı hosted service:
        // biri tıkanınca diğerleri akmaya devam ediyor.
        //
        // Settlement: sağlayıcının batch ödemesi. Cevap yayınlamıyor, karşı taraf
        // yok — tek yönlü bildirim.
        services.AddScoped<ProcessSettlementHandler>();
        services.AddScoped<ProcessInvoiceHandler>();
        services.AddHostedService<SettlementConsumer>();

        services.AddScoped<DebitForWithdrawalHandler>();
        services.AddScoped<RefundWithdrawalHandler>();
        services.AddScoped<SettleWithdrawalHandler>();
        services.AddScoped<DebitSuspenseForReturnHandler>();
        services.AddScoped<SettleDepositReturnHandler>();
        services.AddScoped<RestoreSuspendedDepositHandler>();
        services.AddHostedService<WithdrawalCommandConsumer>();

        // Havale: banka hesabımıza gelen para. Cüzdana mı askıya mı yazılacağına burada
        // karar veriliyor; gönderenin hesap sahibi olduğu onboarding'e soruluyor.
        services.AddScoped<ProcessDepositHandler>();
        services.AddHostedService<DepositConsumer>();
        services.AddHolderIdentity();

        // Kartla yükleme: kart yüklemesi servisi ödemenin sonucunu bildiriyor. Ödendiyse para
        // cüzdana, ödenmediyse limit payı serbest.
        services.AddScoped<ProcessCardTopupHandler>();
        services.AddHostedService<CardTopupConsumer>();

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
    /// Onboarding'e kendi token'ıyla soran istemci. Zaman aşımı kısa: cevap gelmezse havale
    /// kuyruğa geri dönüyor, uzun beklemek arkasındaki havaleleri de bekletirdi.
    /// </summary>
    private static void AddHolderIdentity(this IServiceCollection services)
    {
        services.AddOptions<KeycloakOptions>()
            .BindConfiguration(KeycloakOptions.SectionName)
            .Validate(k => Uri.TryCreate(k.BaseUrl, UriKind.Absolute, out _)
                           && !string.IsNullOrWhiteSpace(k.Realm)
                           && !string.IsNullOrWhiteSpace(k.ClientId)
                           && !string.IsNullOrWhiteSpace(k.ClientSecret),
                $"{KeycloakOptions.SectionName}: BaseUrl, Realm, ClientId ve ClientSecret zorunlu.")
            .ValidateOnStart();

        services.AddHttpClient(KeycloakTokenClient, (provider, http) =>
            http.BaseAddress = WithSlash(provider.GetRequiredService<IOptions<KeycloakOptions>>().Value.BaseUrl));
        services.AddSingleton(provider => new ServiceTokens(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(KeycloakTokenClient),
            provider.GetRequiredService<IOptions<KeycloakOptions>>(),
            TimeProvider.System));
        services.AddTransient<ServiceTokenHandler>();

        services.AddHttpClient<IHolderIdentity, OnboardingHolderIdentity>((provider, http) =>
            {
                http.BaseAddress = WithSlash(provider.GetRequiredService<IConfiguration>()[OnboardingUrlKey]
                                             ?? throw new InvalidOperationException($"Zorunlu konfigürasyon eksik: {OnboardingUrlKey}."));
                http.Timeout = TimeSpan.FromSeconds(5);
            })
            .AddHttpMessageHandler<ServiceTokenHandler>();
    }

    private static Uri WithSlash(string url) => new(url.EndsWith('/') ? url : url + "/");

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

        if (!Uri.TryCreate(app.Configuration[OnboardingUrlKey], UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"Zorunlu konfigürasyon eksik: {OnboardingUrlKey}.");
        }

        return app;
    }
}
