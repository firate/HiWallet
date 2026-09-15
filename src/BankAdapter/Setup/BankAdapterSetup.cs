using HiWallet.BankAdapter.Application;
using HiWallet.BankAdapter.Infrastructure.Callbacks;
using HiWallet.BankAdapter.Infrastructure.Jobs;
using HiWallet.BankAdapter.Infrastructure.Messaging;
using HiWallet.BankIntegration.Setup;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.BankAdapter.Setup;

public static class BankAdapterSetup
{
    public static IServiceCollection AddBankAdapter(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BankAdapterOptions>()
            .Bind(configuration.GetSection(BankAdapterOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.BaseUrl),
                $"{BankAdapterOptions.SectionName}:BaseUrl boş. Bankanın adresi olmadan transfer iletilemez.")
            .Validate(
                options => options.Reconciliation.Interval > TimeSpan.Zero,
                // Tarama KAPATILAMAZ, yalnızca aralığı ayarlanır (decisions.md madde 35).
                // Sıfır ya da negatif aralık PeriodicTimer'ı patlatır ve tarama sessizce
                // hiç koşmaz; kaçırılan her callback kalıcı kayba dönerdi.
                $"{BankAdapterOptions.SectionName}:Reconciliation:Interval pozitif olmalı. " +
                "Mutabakat taraması kapatılamaz — kaçırılan callback kalıcı kayıp olur.")
            .Validate(
                options => options.Reconciliation.StaleAfter > TimeSpan.Zero,
                $"{BankAdapterOptions.SectionName}:Reconciliation:StaleAfter pozitif olmalı.")
            .Validate(
                options => options.Reconciliation.BatchSize is > 0 and <= 1000,
                $"{BankAdapterOptions.SectionName}:Reconciliation:BatchSize 1-1000 aralığında olmalı.")
            // Fail fast: eksik ayar ilk transferde değil, başlangıçta patlasın.
            .ValidateOnStart();

        services.AddBankPersistence();
        services.AddHiWalletJobLease(BankPersistenceSetup.ConnectionStringName);

        services.AddHttpClient(BankClient.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<BankAdapterOptions>>().Value;

            client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");
            client.Timeout = options.RequestTimeout;
        });

        // Singleton: arka plan servisleri kullanıyor ve durumu yok. HttpClient'ı
        // fabrikadan çağrı başına alıyor, dolayısıyla handler rotasyonunu kaçırmıyor.
        services.AddSingleton<BankClient>();
        services.AddSingleton<TransferCompleter>();

        // Scoped: komut tüketicisi her mesaj için kendi scope'unu açıyor.
        services.AddScoped<StartBankTransferHandler>();

        services.AddHostedService<BankCommandConsumer>();
        services.AddHostedService<CallbackRelay>();
        services.AddHostedService<ReplyRelay>();
        services.AddHostedService<ReconciliationScan>();

        services.AddHealthChecks()
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(BankPersistenceSetup.ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3))
            // Broker olmadan komut alınamıyor ve cevap yayınlanamıyor: bu servisin
            // işi tam olarak o ikisi.
            .AddRabbitMqCheck("rabbitmq", HealthStatus.Unhealthy, HealthCheckEndpoints.ReadyTag);

        return services;
    }

    /// <summary>Fail fast (baseline.md madde 1). Host KURULDUKTAN sonra çalışır.</summary>
    public static WebApplication ValidateBankAdapterConfiguration(this WebApplication app)
    {
        BankPersistenceSetup.RequireConnectionString(app.Configuration);

        return app;
    }
}
