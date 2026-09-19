using System.Threading.RateLimiting;
using HiWallet.Shared.Infrastructure.RateLimiting;

namespace HiWallet.WithdrawalOrchestrator.Setup;

/// <summary>
/// Çekim endpoint'inin rate limit'i (baseline.md madde 7). <c>wallet-api</c> ile aynı
/// erişim seviyesi: public ve müşteriye dönük.
///
/// Anahtar IP. Authn yok (decisions.md madde 12), yani bölünecek bir hesap kimliği
/// de yok; IP paylaşan müşteriler (kurumsal NAT, mobil operatör) aynı kovayı
/// kullanıyor. <c>wallet-api</c>'nin transfer limitiyle aynı kabul.
///
/// Limit transferinkinden düşük: çekim dışarıya para çıkarıyor ve bir müşterinin
/// dakikada onlarca çekim başlatması gerçek bir kullanım değil.
///
/// In-memory, çok instance'ta efektif limit instance başına (decisions.md madde 12).
/// </summary>
public static class RateLimitingSetup
{
    public const string WithdrawalsPolicy = "withdrawals";

    public const string Section = "RateLimiting:Withdrawals";

    public static IServiceCollection AddOrchestratorRateLimiting(
        this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(WithdrawalsPolicy, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => TokenBucketLimits.Read(context, Section, burstSize: 10, sustainedPerMinute: 30)));

            options.OnRejected = RateLimitRejection.WriteAsync;
        });

        return services;
    }
}
