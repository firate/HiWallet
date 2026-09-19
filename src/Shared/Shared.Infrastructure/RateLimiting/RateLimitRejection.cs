using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Limit aşıldığında dönen <c>429</c>: <c>Retry-After</c> başlığı + ProblemDetails
/// gövdesi (baseline.md madde 5 ve 7). Servisler politikalarını kendileri kuruyor —
/// kimin kovası neye göre bölünüyor servise özgü — ama reddin şekli her yerde aynı.
/// </summary>
public static class RateLimitRejection
{
    public static async ValueTask WriteAsync(OnRejectedContext context, CancellationToken ct)
    {
        // Retry-After olmadan client ne zaman deneyeceğini bilemez ve genelde hemen
        // tekrar dener.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
            ? value
            : TimeSpan.FromMinutes(1);

        var seconds = (int)retryAfter.TotalSeconds;

        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        context.HttpContext.Response.ContentType = "application/problem+json";

        await context.HttpContext.Response.WriteAsync(
            $$"""
              {"type":"https://hiwallet.dev/problems/rate-limit",
               "title":"Çok fazla istek",
               "status":429,
               "detail":"İstek sınırı aşıldı. {{seconds}} saniye sonra tekrar deneyin."}
              """, ct);
    }
}

/// <summary>Token bucket ayarı. Her servis kendi bölümünden bağlıyor.</summary>
public sealed class TokenBucketLimits
{
    /// <summary>Anlık patlama kapasitesi.</summary>
    public int BurstSize { get; init; }

    /// <summary>Dakikada yenilenen token sayısı — sürdürülebilir hız.</summary>
    public int SustainedPerMinute { get; init; }

    public TokenBucketRateLimiterOptions ToOptions() => new()
    {
        TokenLimit = BurstSize,
        TokensPerPeriod = SustainedPerMinute,
        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
