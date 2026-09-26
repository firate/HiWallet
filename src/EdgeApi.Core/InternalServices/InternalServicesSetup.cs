using HiWallet.EdgeApi.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace HiWallet.EdgeApi.InternalServices;

/// <summary>
/// İç servis istemcileri. Her ön API yalnızca kullandığı servisi kaydediyor; adresi
/// eksik olan servis uygulamayı açtırmıyor.
/// </summary>
public static class InternalServicesSetup
{
    public static IHttpClientBuilder AddWalletApiClient(this IServiceCollection services) =>
        services.AddInternalService<WalletApiClient>("WalletApi");

    public static IHttpClientBuilder AddWithdrawalOrchestratorClient(this IServiceCollection services) =>
        services.AddInternalService<WithdrawalOrchestratorClient>("WithdrawalOrchestrator");

    /// <summary>
    /// Pipeline <c>bank-adapter</c>'ınkiyle aynı sırada: toplam zaman aşımı, yeniden
    /// deneme, devre kesici, deneme zaman aşımı.
    ///
    /// <b>POST yeniden DENENMİYOR.</b> İç servis <c>Idempotency-Key</c> ile tekrarı
    /// zaten güvenli kılıyor, ama tekrarın sahibi istemci: zaman aşımına uğrayan bir
    /// istekte ön API da yeniden deneseydi iç servise giden yük her katmanda katlanırdı
    /// ve istemci cevabı yine geç alırdı.
    /// </summary>
    private static IHttpClientBuilder AddInternalService<TClient>(this IServiceCollection services, string name)
        where TClient : InternalServiceClient
    {
        var section = $"{InternalServiceOptions.SectionPrefix}:{name}";

        services.AddOptions<InternalServiceOptions>(name)
            .BindConfiguration(section)
            .Validate(
                options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                $"{section}:BaseUrl boş ya da mutlak bir adres değil. İç servisin adresi olmadan ön API hiçbir isteği iletemez.")
            .Validate(
                options => options.RequestTimeout > TimeSpan.Zero,
                $"{section}:RequestTimeout pozitif olmalı.")
            // Fail fast: eksik ayar ilk istekte değil, başlangıçta patlasın.
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, InternalServiceExceptionHandler>());

        var builder = services.AddHttpClient<TClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<InternalServiceOptions>>().Get(name);

            client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");

            // Süreyi pipeline yönetiyor. HttpClient.Timeout bütün denemeleri birlikte
            // keserdi ve ikinci deneme daha başlamadan iptal olurdu.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        builder.AddStandardResilienceHandler().Configure((options, provider) =>
        {
            var service = provider.GetRequiredService<IOptionsMonitor<InternalServiceOptions>>().Get(name);

            options.AttemptTimeout.Timeout = service.RequestTimeout;

            options.Retry.DisableForUnsafeHttpMethods();

            // Karşısında bir insan bekliyor: iki kısa deneme anlık kesintiyi kapatıyor,
            // süren kesintide istemci hızla 503 alıyor.
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.FromMilliseconds(100);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;

            // Pipeline'ın kuralı: örnekleme süresi deneme süresinin en az iki katı.
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(
                Math.Max(30, service.RequestTimeout.TotalSeconds * 2));

            options.TotalRequestTimeout.Timeout =
                service.RequestTimeout * (options.Retry.MaxRetryAttempts + 1) + TimeSpan.FromSeconds(1);
        });

        return builder;
    }
}
