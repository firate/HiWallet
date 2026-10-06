using HiWallet.CardTopup.Application;
using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.Extensions.Options;

namespace HiWallet.CardTopup.Infrastructure.Jobs;

/// <summary>
/// Açık yüklemelerin periyodik taraması (<see cref="OpenCardTopupScanner"/>). Opsiyonel bir
/// iyileştirme DEĞİL: oturumun süresi dolduğunda sağlayıcı bildirim göndermiyor ve bunu
/// öğrenmenin başka yolu yok. Koşmasa her terk edilen ödeme sayfası müşterinin limit payını
/// kalıcı olarak tutardı.
/// </summary>
internal sealed class OpenCardTopupScan(
    OpenCardTopupScanner scanner,
    JobLease lease,
    IOptions<CardTopupOptions> options,
    ILogger<OpenCardTopupScan> logger) : ScheduledJob(lease, logger)
{
    protected override string Name => "card-topup:open-scan";

    protected override TimeSpan Interval => options.Value.Scan.Interval;

    protected override async Task RunAsync(CancellationToken ct)
    {
        var report = await scanner.ScanAsync(ct);

        if (report.Abandoned > 0)
        {
            // Warning: pay isteği cevapsız kalmış. Tek tük olabilir, sürekli olursa wallet-api'ye
            // giden yol bozuk.
            logger.LogWarning("{Count} terk edilmiş kartla yükleme kapatıldı.", report.Abandoned);
        }

        if (report.Closed > 0)
        {
            logger.LogInformation("{Count} kartla yükleme sağlayıcıya sorularak kapatıldı.", report.Closed);
        }
    }
}
