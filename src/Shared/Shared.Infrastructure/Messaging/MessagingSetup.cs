using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.Shared.Infrastructure.Messaging;

public static class MessagingSetup
{
    /// <param name="clientName">
    /// Broker'ın yönetim arayüzünde görünecek ad. Her servis kendi adını verir;
    /// "bu bağlantıyı kim açtı" sorusu üretimde ilk sorulan şey.
    /// </param>
    public static IServiceCollection AddHiWalletMessaging(
        this IServiceCollection services, IConfiguration configuration, string clientName)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Configure(options => options.ClientName = clientName)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Host),
                $"{RabbitMqOptions.SectionName}:Host boş. Container'da .env üzerinden verilir.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Username)
                           && !string.IsNullOrWhiteSpace(options.Password),
                $"{RabbitMqOptions.SectionName}:Username / Password boş.")
            .Validate(
                options => options.Port is > 0 and <= 65535,
                $"{RabbitMqOptions.SectionName}:Port geçersiz.")
            .Validate(
                options => options.PartitionCount is >= 1 and <= 64,
                $"{RabbitMqOptions.SectionName}:PartitionCount 1-64 aralığında olmalı.")
            // Fail fast: eksik broker adresi ilk mesajda değil, başlangıçta patlasın.
            .ValidateOnStart();

        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<TopupTopology>();

        return services;
    }

    /// <summary>
    /// Broker'a bağlanabiliyor muyuz.
    /// </summary>
    /// <param name="failureStatus">
    /// Broker'ın o servis için ne kadar hayati olduğuna göre seçilir ve seçim
    /// önemli: <c>Unhealthy</c> servisi trafikten çektirir. Broker yalnızca bir
    /// yan akış için gerekiyorsa <c>Degraded</c> doğrusu — durum sağlık çıktısında
    /// görünür ama uç 200 dönmeye devam eder, çalışan yollar kapanmaz.
    /// </param>
    public static IHealthChecksBuilder AddRabbitMqCheck(
        this IHealthChecksBuilder builder,
        string name,
        HealthStatus failureStatus,
        params string[] tags) =>
        builder.AddCheck<RabbitMqHealthCheck>(
            name,
            failureStatus,
            tags,
            // Probe asılı kalmasın; broker yavaşsa readiness hızlıca cevap versin.
            timeout: TimeSpan.FromSeconds(3));
}
