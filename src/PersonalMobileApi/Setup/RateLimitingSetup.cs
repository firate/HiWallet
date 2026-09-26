using System.Threading.RateLimiting;
using HiWallet.Shared.Infrastructure.RateLimiting;

namespace HiWallet.PersonalMobileApi.Setup;

/// <summary>
/// Müşteri başına rate limit (baseline.md madde 7). İstemciyi tanıyan ön API, sınır
/// burada: iç servisler yalnızca ön API'lerin adresini görüyor.
///
/// Anahtar IP. Kimlik doğrulama geldiğinde anahtar müşteri kimliği olacak: IP paylaşan
/// müşteriler (mobil operatör, kurumsal NAT) bugün aynı kovayı kullanıyor.
///
/// In-memory, çok instance'ta efektif limit instance başına (decisions.md madde 12).
/// </summary>
public static class RateLimitingSetup
{
    public const string CustomerPolicy = "customer";

    /// <summary>
    /// Çekim başlatmanın ayrı ve daha dar kovası: dışarıya para çıkarıyor ve bir
    /// müşterinin dakikada onlarca çekim başlatması gerçek bir kullanım değil.
    /// </summary>
    public const string WithdrawalsPolicy = "withdrawals";

    public static IServiceCollection AddPersonalMobileRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(CustomerPolicy, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    ClientKey(context),
                    _ => TokenBucketLimits.Read(context, "RateLimiting:Customer", burstSize: 20, sustainedPerMinute: 60)));

            options.AddPolicy(WithdrawalsPolicy, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    ClientKey(context),
                    _ => TokenBucketLimits.Read(context, "RateLimiting:Withdrawals", burstSize: 10, sustainedPerMinute: 30)));

            options.OnRejected = RateLimitRejection.WriteAsync;
        });

        return services;
    }

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
