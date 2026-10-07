using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// Ödemeleri kampanyalara göre değerlendirir (decisions.md madde 37). Tek instance
/// <see cref="JobLease"/> ile: bütçe ve tavanlar sıralı değerlendirmeye dayanıyor,
/// iki instance aynı kalanı aynı anda harcayabilirdi.
/// </summary>
internal sealed class PromoCampaignJob(
    PromoCampaignEvaluator evaluator,
    JobLease lease,
    TimeProvider timeProvider,
    IOptions<PromoCampaignOptions> options,
    ILogger<PromoCampaignJob> logger) : ScheduledJob(lease, logger)
{
    protected override string Name => "wallet:promo-campaign";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task RunAsync(CancellationToken ct)
    {
        var granted = await evaluator.EvaluateAsync(
            timeProvider.GetUtcNow(), options.Value.Lookback, options.Value.BatchSize, ct);

        if (granted > 0)
        {
            logger.LogInformation("Kampanyalardan {Count} promo partisi açıldı.", granted);
        }
    }
}
