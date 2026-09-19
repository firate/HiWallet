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

    public const string Section = "RateLimiting:Webhooks";

    public static IServiceCollection AddTopupRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(WebhookPolicy, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    context.Request.RouteValues["provider"]?.ToString() ?? "unknown",
                    _ => TokenBucketLimits.Read(context, Section, burstSize: 100, sustainedPerMinute: 600)));

            options.OnRejected = RateLimitRejection.WriteAsync;
        });

        return services;
    }
}
