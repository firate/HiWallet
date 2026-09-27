using System.Threading.RateLimiting;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.Shared.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.EdgeApi.RateLimiting;

/// <summary>
/// İstemci başına rate limit (baseline.md madde 7). İstemciyi tanıyan ön API, sınır
/// burada: iç servisler yalnızca ön API'lerin adresini görüyor.
///
/// Anahtar token'daki kimlik: IP paylaşan müşteriler (mobil operatör, kurumsal NAT)
/// birbirinin limitini yemiyor, bir işyerinin patlaması diğerinin entegrasyonunu
/// durdurmuyor. Kimliksiz istek IP'nin kovasına düşüyor; limiter kimlik kontrolünden
/// önce koştuğu için geçersiz token'la yapılan istek de sınırlanıyor.
///
/// Limitler ön API'ye özgü, kovaların şekli değil: her ön API varsayılanını kendisi
/// veriyor, ayarı <c>RateLimiting:Client</c> ve <c>RateLimiting:Withdrawals</c>'tan okunuyor.
///
/// In-memory, çok instance'ta efektif limit instance başına (decisions.md madde 12).
/// </summary>
public static class EdgeRateLimiting
{
    public const string ClientPolicy = "client";

    /// <summary>
    /// Çekim başlatmanın ayrı ve daha dar kovası: dışarıya para çıkarıyor ve dakikada
    /// onlarca çekim başlatmak gerçek bir kullanım değil. Kova ayrı olduğu için çekim
    /// sınırına takılan istemci bakiyesini görmeye devam ediyor.
    /// </summary>
    public const string WithdrawalsPolicy = "withdrawals";

    public static IServiceCollection AddEdgeRateLimiting(
        this IServiceCollection services, BucketDefaults client, BucketDefaults withdrawals)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(ClientPolicy, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    ClientKey(context),
                    _ => TokenBucketLimits.Read(
                        context, "RateLimiting:Client", client.BurstSize, client.SustainedPerMinute)));

            options.AddPolicy(WithdrawalsPolicy, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    ClientKey(context),
                    _ => TokenBucketLimits.Read(
                        context, "RateLimiting:Withdrawals", withdrawals.BurstSize, withdrawals.SustainedPerMinute)));

            options.OnRejected = RateLimitRejection.WriteAsync;
        });

        return services;
    }

    private static string ClientKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? "sub:" + context.User.Subject()
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}

/// <summary>Kovanın varsayılanı: anlık patlama ve dakikada yenilenen istek sayısı.</summary>
public sealed record BucketDefaults(int BurstSize, int SustainedPerMinute);
