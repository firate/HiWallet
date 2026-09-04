using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HiWallet.WalletService.Setup;

/// <summary>
/// ASP.NET Core built-in rate limiting (baseline.md madde 7). In-memory.
///
/// KABUL EDİLEN SINIRLAMA: limiter in-memory olduğu için çok instance'ta efektif limit
/// instance BAŞINA uygulanır — üç instance varsa müşteri üç katını harcayabilir.
/// Dağıtık limiter Redis gerektirir ve bu projenin konusu değil
/// (decisions.md madde 12).
/// </summary>
public static class RateLimitingSetup
{
    public const string TransfersPolicy = "transfers";

    public static IServiceCollection AddHiWalletRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.Section));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(TransfersPolicy, context =>
            {
                var limits = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>().Value;

                // Partition anahtarı şimdilik IP. Authn eklendiğinde hesap kimliği
                // olmalı — IP paylaşan müşteriler (kurumsal NAT, mobil operatör)
                // birbirinin limitini yiyor. Authn bu projenin kapsamı dışında
                // (decisions.md madde 12), o yüzden şimdilik bilinçli olarak IP.
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        // Token bucket, fixed window yerine: pencere sınırında iki katı
                        // isteğin geçmesine izin vermiyor ve kısa patlamaları da
                        // boğmuyor. Para transferi tam olarak böyle bir trafik.
                        TokenLimit = limits.BurstSize,
                        TokensPerPeriod = limits.SustainedPerMinute,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.OnRejected = async (context, ct) =>
            {
                // Retry-After olmadan client ne zaman deneyeceğini bilemez ve
                // genelde hemen tekrar dener (baseline.md madde 7).
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

public sealed class RateLimitOptions
{
    public const string Section = "RateLimiting:Transfers";

    /// <summary>Anlık patlama kapasitesi.</summary>
    public int BurstSize { get; init; } = 20;

    /// <summary>Dakikada yenilenen token sayısı — sürdürülebilir hız.</summary>
    public int SustainedPerMinute { get; init; } = 60;
}
