using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// İşletme günlük özetini periyodik olarak yeniden üretir (<c>overview.md</c> madde 7).
///
/// <b>Yalnızca KAPALI günler.</b> Bugünün özeti hesaplanmıyor: gün dolmadan yazılan
/// satır eksik olur ve "rapor var ama yanlış" durumu, "rapor henüz yok" durumundan
/// daha kötüdür — kimse tazeliğini sorgulamaz.
///
/// <b>Son N kapalı gün her turda yeniden hesaplanıyor.</b> Kapalı bir günün
/// ledger'ı değişmiyor, yani yeniden hesaplamak aynı sonucu veriyor; karşılığında
/// kısa kesintilerde eksik kalan gün kendiliğinden doluyor.
/// </summary>
internal sealed class BusinessSummaryJob(
    BusinessSummaryCalculator calculator,
    JobLease lease,
    TimeProvider timeProvider,
    IOptions<BusinessSummaryOptions> options,
    ILogger<BusinessSummaryJob> logger) : ScheduledJob(lease, logger)
{
    protected override string Name => "wallet:business-daily-summary";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var lookback = Math.Max(1, options.Value.LookbackDays);

        // En eskiden yeniye: bir gün patlarsa sonrakiler yine de denensin ve
        // boşluk en eski günde kalsın.
        for (var offset = lookback; offset >= 1; offset--)
        {
            var day = today.AddDays(-offset);
            var written = await calculator.CalculateAsync(day, ct);

            logger.LogInformation(
                "{Day} için {Count} işletme özeti hesaplandı.", day, written);
        }
    }
}
