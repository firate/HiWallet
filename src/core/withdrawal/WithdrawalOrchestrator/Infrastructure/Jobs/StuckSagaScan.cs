using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.Extensions.Options;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Jobs;

/// <summary>
/// Terminal olmayan bir durumda asılı kalmış çekimleri periyodik olarak raporlar.
///
/// <b>Bu tarama opsiyonel bir iyileştirme değil.</b> Saga'nın kendi veritabanında,
/// wallet'ın ledger'ından ayrı durmasının (madde 7 ve 33) faturası iki veritabanı
/// arasında ayrışma ihtimali: wallet parayı düşmüş ama cevabı kaybolmuşsa saga
/// <c>debited</c>'da kalıyor, banka komutu tüketilmemişse
/// <c>bank_transfer_pending</c>'de. İkisinde de HATA LOG'U YOK — hiçbir şey
/// patlamadı, yalnızca bir cevap gelmedi. Saga "bekliyor" görünüyor, müşteri
/// parasını göremiyor ve bunu yakalayacak tek mekanizma bu tarama (madde 33).
///
/// <b>Düzeltmiyor, gösteriyor.</b> Otomatik ilerletmek yanlış olurdu: taramanın
/// bildiği tek şey "uzun süredir değişmedi", nedenini bilmiyor. Kayıp bir cevabı
/// varsayıp saga'yı ilerletmek, cevabın yalnızca gecikmiş olduğu durumda işi ikinci
/// kez yaptırırdı — dışarıya para çıkaran bir akışta kabul edilemez. Aynı gerekçe
/// madde 31'in <c>Conflict</c> kararında da geçiyor: sistem düzeltmiyor, gösteriyor.
/// </summary>
internal sealed class StuckSagaScan(
    StuckSagaScanner scanner,
    JobLease lease,
    IOptions<StuckSagaScanOptions> options,
    ILogger<StuckSagaScan> logger) : ScheduledJob(lease, logger)
{
    protected override string Name => "withdrawal:stuck-saga-scan";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task RunAsync(CancellationToken ct)
    {
        var report = await scanner.ScanAsync(ct);

        if (report.IsEmpty)
        {
            logger.LogDebug("Takılmış çekim yok.");
            return;
        }

        // Alarm. Warning DEĞİL Error: burada duran her satır bir müşterinin
        // görünmeyen parası ve kendiliğinden çözülmüyor.
        logger.LogError(
            "{Count} çekim {Threshold} süredir ilerlemiyor. En eskiler: {Sagas}",
            report.Total,
            scanner.Threshold,
            string.Join(", ", report.Oldest.Select(
                saga => $"{saga.SagaId} [{saga.State.ToText()}] {saga.UpdatedAt:O}")));
    }
}
