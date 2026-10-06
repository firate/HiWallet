using HiWallet.CardTopup.Api.Errors;
using HiWallet.CardTopup.Application;
using HiWallet.CardTopup.Infrastructure.Jobs;
using HiWallet.CardTopup.Infrastructure.Messaging;
using HiWallet.CardTopup.Infrastructure.Persistence;
using HiWallet.CardTopup.Infrastructure.Upstream;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace HiWallet.CardTopup.Setup;

public static class CardTopupSetup
{
    /// <summary>Bu servisin kendi veritabanı. Wallet şemasına erişimi yok.</summary>
    public const string ConnectionStringName = "CardTopup";

    /// <summary>wallet-api'nin adresi; ön API'lerle aynı ayar yolu.</summary>
    public const string WalletApiSection = "InternalServices:WalletApi";

    public static IServiceCollection AddCardTopup(this IServiceCollection services)
    {
        services.AddOptions<CardTopupOptions>()
            .BindConfiguration(CardTopupOptions.SectionName)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Provider),
                $"{CardTopupOptions.SectionName}:Provider boş. Pay ve para hangi sağlayıcıya yazılacak?")
            .Validate(
                options => Uri.TryCreate(options.ProviderUrl, UriKind.Absolute, out _),
                $"{CardTopupOptions.SectionName}:ProviderUrl boş ya da mutlak bir adres değil.")
            .Validate(
                options => options.RequestTimeout > TimeSpan.Zero && options.SessionLifetime > TimeSpan.Zero,
                $"{CardTopupOptions.SectionName}: RequestTimeout ve SessionLifetime pozitif olmalı.")
            .Validate(
                options => options.Scan.Interval > TimeSpan.Zero
                           && options.Scan.CreatedStaleAfter > TimeSpan.Zero
                           && options.Scan.ExpiryGrace >= TimeSpan.Zero
                           && options.Scan.BatchSize is > 0 and <= 1000,
                // Tarama KAPATILAMAZ: süresi dolan ödemeyi sağlayıcı bildirmiyor.
                $"{CardTopupOptions.SectionName}:Scan: Interval ve CreatedStaleAfter pozitif, BatchSize 1-1000 " +
                "olmalı. Tarama kapatılamaz — süresi dolan ödemenin payı kalıcı olurdu.")
            .ValidateOnStart();

        // Yalnızca factory: relay, tüketici ve tarama istek kapsamı dışında.
        services.AddDbContextFactory<CardTopupDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            options.UseNpgsql(
                configuration.GetConnectionString(ConnectionStringName),
                npgsql => npgsql.CommandTimeout(DbTimeouts.CommandSeconds));
        });

        services.AddWalletHoldClient();
        services.AddCardPaymentClient();

        services.AddSingleton<CardTopupTransitions>();
        services.AddSingleton<OpenCardTopupScanner>();
        services.AddScoped<StartCardTopupHandler>();
        services.AddScoped<ApplyCardPaymentHandler>();
        services.AddScoped<CardTopupQueries>();

        services.AddSingleton<IExceptionHandler, UpstreamExceptionHandler>();

        services.AddHostedService<CardTopupOutboxRelay>();
        services.AddHostedService<CardPaymentConsumer>();

        services.AddHiWalletJobLease(ConnectionStringName);
        services.AddHostedService<OpenCardTopupScan>();

        services.AddHealthChecks()
            // Postgres olmadan yükleme başlatılamaz ve kapatılamaz.
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3))
            // Broker olmadan yükleme başlatılmaya DEVAM eder: kapanış outbox'ta birikir.
            .AddRabbitMqCheck("rabbitmq", HealthStatus.Degraded, HealthCheckEndpoints.ReadyTag);

        return services;
    }

    /// <summary>
    /// wallet-api'nin pay ucu. Ön API'lerin iç servis istemcisiyle aynı pipeline: POST yeniden
    /// DENENMİYOR, tekrarın sahibi istemci; karşısında bir insan bekliyor.
    /// </summary>
    private static void AddWalletHoldClient(this IServiceCollection services)
    {
        services.AddOptions<WalletApiOptions>()
            .BindConfiguration(WalletApiSection)
            .Validate(
                options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                $"{WalletApiSection}:BaseUrl boş ya da mutlak bir adres değil. Limit payı istenemez.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddTransient<ForwardAccessTokenHandler>();

        var builder = services.AddHttpClient<WalletHoldClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<WalletApiOptions>>().Value;

            client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");

            // Süreyi pipeline yönetiyor.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        builder.AddHttpMessageHandler<ForwardAccessTokenHandler>();

        builder.AddStandardResilienceHandler().Configure((options, provider) =>
        {
            var wallet = provider.GetRequiredService<IOptions<WalletApiOptions>>().Value;

            options.AttemptTimeout.Timeout = wallet.RequestTimeout;
            options.Retry.DisableForUnsafeHttpMethods();
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(
                Math.Max(30, wallet.RequestTimeout.TotalSeconds * 2));
            options.TotalRequestTimeout.Timeout = wallet.RequestTimeout + TimeSpan.FromSeconds(1);
        });
    }

    /// <summary>
    /// Kart sağlayıcısı. Açılış POST'u yeniden denenmiyor (karşısında bir insan bekliyor ve
    /// tekrar aynı anahtarla istemcinin işi); taramanın sorguları deneniyor.
    /// </summary>
    private static void AddCardPaymentClient(this IServiceCollection services)
    {
        services.AddSingleton<CardPaymentClient>();

        var builder = services.AddHttpClient(CardPaymentClient.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<CardTopupOptions>>().Value;

            client.BaseAddress = new Uri(options.ProviderUrl!.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        builder.AddStandardResilienceHandler().Configure((options, provider) =>
        {
            var settings = provider.GetRequiredService<IOptions<CardTopupOptions>>().Value;

            options.AttemptTimeout.Timeout = settings.RequestTimeout;

            options.Retry.DisableForUnsafeHttpMethods();
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.FromMilliseconds(200);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;

            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(
                Math.Max(30, settings.RequestTimeout.TotalSeconds * 2));

            options.TotalRequestTimeout.Timeout =
                settings.RequestTimeout * (options.Retry.MaxRetryAttempts + 1) + TimeSpan.FromSeconds(2);
        });
    }
}

public sealed class WalletApiOptions
{
    public string? BaseUrl { get; init; }

    /// <summary>Tek çağrının üst sınırı; aşıldığında istemci <c>503</c> alıyor.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
