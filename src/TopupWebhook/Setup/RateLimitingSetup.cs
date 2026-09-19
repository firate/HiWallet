using System.Threading.RateLimiting;
using HiWallet.Shared.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.TopupWebhook.Setup;

/// <summary>
/// Webhook endpoint'i kimliği doğrulanmamış trafiğe açık ve her request'te gövdenin tamamı
/// üzerinde HMAC hesaplıyor — yani doğrudan CPU harcatılabilen bir yüzey. Sınır
/// bu yüzden var (baseline.md madde 7).
///
/// Anahtar sağlayıcı adı, IP değil: sağlayıcı kendi altyapısından, değişken IP'lerle
/// gönderiyor ve bir sağlayıcının patlaması diğerini etkilememeli. IP'ye göre
/// bölmek tek bir sağlayıcının tüm trafiğini tek partition'a yığardı.
///
/// In-memory, çok instance'ta efektif limit instance başına (decisions.md madde 12).
/// </summary>
public static class RateLimitingSetup
{
    public const string WebhookPolicy = "webhooks";

    public static IServiceCollection AddTopupRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        var limits = new WebhookRateLimitOptions();
        configuration.GetSection(WebhookRateLimitOptions.Section).Bind(limits);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(WebhookPolicy, context =>
            {
                var provider = context.Request.RouteValues["provider"]?.ToString() ?? "unknown";

                return RateLimitPartition.GetTokenBucketLimiter(provider, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = limits.BurstSize,
                        TokensPerPeriod = limits.SustainedPerMinute,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.OnRejected = RateLimitRejection.WriteAsync;
        });

        return services;
    }
}

public sealed class WebhookRateLimitOptions
{
    public const string Section = "RateLimiting:Webhooks";

    public int BurstSize { get; init; } = 100;

    public int SustainedPerMinute { get; init; } = 600;
}
