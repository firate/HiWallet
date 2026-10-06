using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// Süresi dolan promo partilerini kapatır (decisions.md madde 37). Tekilliği
/// <see cref="JobLease"/> sağlıyor; iki instance aynı partiyi kapatmaya çalışsa bile
/// idempotency anahtarı <c>promo-expiry:{grant_id}</c> ikinci kaydı engelliyor.
/// </summary>
internal sealed class PromoExpiryJob(
    PromoExpirer expirer,
    JobLease lease,
    TimeProvider timeProvider,
    IOptions<PromoExpiryOptions> options,
    ILogger<PromoExpiryJob> logger) : ScheduledJob(lease, logger)
{
    protected override string Name => "wallet:promo-expiry";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task RunAsync(CancellationToken ct)
    {
        var expired = await expirer.ExpireDueAsync(timeProvider.GetUtcNow(), options.Value.BatchSize, ct);

        // Bulgu yoksa log da yok: süresi dolan parti her turda olmuyor.
        if (expired > 0)
        {
            logger.LogInformation("{Count} promo partisi süresi dolduğu için kapatıldı.", expired);
        }
    }
}
