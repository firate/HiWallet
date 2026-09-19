using HiWallet.BankAdapter.Application;
using HiWallet.BankAdapter.Infrastructure.Callbacks;
using HiWallet.BankAdapter.Infrastructure.Jobs;
using HiWallet.BankAdapter.Infrastructure.Messaging;
using HiWallet.BankIntegration.Setup;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using System.Net;

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

        services.AddBankHttpClient();

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

    /// <summary>
    /// Bankaya giden istemci ve üstündeki resilience pipeline'ı (baseline.md madde 11).
    ///
    /// Sırayla: toplam timeout, retry, circuit breaker, deneme timeout'u. Pipeline
    /// Polly v8 üzerinde koşuyor; <c>Microsoft.Extensions.Http.Resilience</c> onu
    /// <c>HttpClient</c>'a bağlayan katman.
    ///
    /// <b>POST yeniden denenebiliyor</b> çünkü transfer request'i
    /// <c>Idempotency-Key</c> taşıyor ve banka aynı anahtarla ikinci transfer
    /// açmıyor (decisions.md madde 35). Anahtar olmasaydı yeniden deneme
    /// müşterinin parasını iki kez gönderirdi.
    ///
    /// Testte de bu metot kullanılıyor: sınanan şey resilience'ın kurulduğu kod.
    /// </summary>
    internal static IHttpClientBuilder AddBankHttpClient(this IServiceCollection services)
    {
        var builder = services.AddHttpClient(BankClient.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<BankAdapterOptions>>().Value;

            client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");

            // Süreyi pipeline yönetiyor. HttpClient.Timeout bütün denemeleri birlikte
            // keserdi ve ikinci deneme daha başlamadan iptal olurdu.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        builder.AddStandardResilienceHandler().Configure((options, provider) =>
        {
            var bank = provider.GetRequiredService<IOptions<BankAdapterOptions>>().Value;

            // Tek denemenin sınırı; aşıldığında deneme geçici hata sayılıyor.
            options.AttemptTimeout.Timeout = bank.RequestTimeout;

            // İki yeniden deneme: anlık kesinti burada kapanıyor, süren kesinti
            // mesajın kuyruğa dönmesiyle. Bekleme kısa çünkü mesaj bu sırada
            // tüketicinin elinde duruyor.
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.FromMilliseconds(200);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.Retry.ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome));

            // Banka uzun süre cevap vermiyorsa devre açılıyor ve çağrılar beklemeden
            // düşüyor. Açık devre de geçici hata: mesaj kuyrukta kalıyor.
            options.CircuitBreaker.ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome));
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(
                Math.Max(30, bank.RequestTimeout.TotalSeconds * 2));

            // Bütün denemelerin üst sınırı. Deneme sınırının altında kalamaz.
            options.TotalRequestTimeout.Timeout =
                bank.RequestTimeout * (options.Retry.MaxRetryAttempts + 1) + TimeSpan.FromSeconds(2);
        });

        return builder;
    }

    /// <summary>
    /// Yeniden denemeye değen sonuçlar. <see cref="BankClient"/>'ın ölçütüyle aynı:
    /// cevap alamamak ve bankanın "şu an olmaz" demesi geçici, kalıcı cevap değil.
    /// </summary>
    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutRejectedException
        || outcome.Result is { } response
        && ((int)response.StatusCode >= 500
            || response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout);

    /// <summary>Fail fast (baseline.md madde 1). Host KURULDUKTAN sonra çalışır.</summary>
    public static WebApplication ValidateBankAdapterConfiguration(this WebApplication app)
    {
        BankPersistenceSetup.RequireConnectionString(app.Configuration);

        return app;
    }
}
