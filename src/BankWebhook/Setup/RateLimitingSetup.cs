using System.Threading.RateLimiting;
using HiWallet.Shared.Infrastructure.RateLimiting;

namespace HiWallet.BankWebhook.Setup;

/// <summary>
/// Bankanın callback endpoint'inin rate limit'i (baseline.md madde 7). Endpoint her
/// request'te gövdenin tamamı üzerinde HMAC hesaplıyor; imzası geçersiz request'ler
/// de o hesabı yaptırıyor. Ağ tarafında IP kısıtlı, bu sınır onun arkasında ikinci kat.
///
/// Anahtar banka adı (route'taki <c>{bank}</c>), IP değil: banka kendi altyapısından,
/// değişken IP'lerle gönderiyor ve bir bankanın patlaması diğerini etkilememeli.
/// <c>topup-webhook</c>'taki sağlayıcı ayrımının aynısı.
///
/// Reddedilen callback kaybolmuyor: banka <c>429</c>'u geçici sayıp yeniden
/// gönderiyor, göndermezse mutabakat taraması transferi bankaya soruyor
/// (decisions.md madde 35).
///
/// In-memory, çok instance'ta efektif limit instance başına (decisions.md madde 12).
/// </summary>
public static class RateLimitingSetup
{
    public const string CallbacksPolicy = "bank-callbacks";

    public const string Section = "RateLimiting:BankCallbacks";

    public static IServiceCollection AddBankWebhookRateLimiting(
        this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(CallbacksPolicy, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    context.Request.RouteValues["bank"]?.ToString() ?? "unknown",
                    _ => TokenBucketLimits.Read(context, Section, burstSize: 100, sustainedPerMinute: 600)));

            options.OnRejected = RateLimitRejection.WriteAsync;
        });

        return services;
    }
}
