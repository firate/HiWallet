using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.TopupWebhook.Setup;

/// <summary>
/// Webhook ucu kimliği doğrulanmamış trafiğe açık ve her istekte gövdenin tamamı
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

            options.OnRejected = async (context, ct) =>
            {
                // Retry-After olmadan sağlayıcı ne zaman deneyeceğini bilemez.
                // Webhook'ta bu özellikle önemli: çoğu sağlayıcı 429'u geçici sayıp
                // tekrar dener, ama ne kadar sonra deneyeceğini başlıktan öğrenir.
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
                    ? value
                    : TimeSpan.FromMinutes(1);

                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);

                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsync(
                    $$"""
                      {"type":"https://hiwallet.dev/problems/rate-limit",
                       "title":"Çok fazla istek",
                       "status":429,
                       "detail":"İstek sınırı aşıldı. {{(int)retryAfter.TotalSeconds}} saniye sonra tekrar deneyin."}
                      """, ct);
            };
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
