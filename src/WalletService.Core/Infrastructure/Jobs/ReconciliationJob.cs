using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// Mutabakat raporu (<c>overview.md</c> madde 7). Clearing hesabını settlement
/// kayıtlarıyla, beklenen ücretleri faturalarla karşılaştırıyor.
///
/// <b>Sistem düzeltmiyor, gösteriyor.</b> Takılmış saga taraması ve fatura
/// uyuşmazlığıyla aynı ilke (madde 11, madde 33): bulgunun hangi taraftan geldiğine
/// sistem karar veremez. Otomatik düzeltme, en iyi ihtimalle doğru olan bir tahmini
/// ledger'a yazmak olurdu ve ledger'da yazan her şeyin gerçekleşmiş olması gerekiyor.
///
/// Bulguların ağırlığı FARKLI, o yüzden seviyeler de farklı:
/// projeksiyon ayrışması <c>Error</c> (kodda hata var, veriye güvenilemez),
/// diğerleri <c>Warning</c> (dış dünya gecikmiş ya da bir insan bakmalı).
/// </summary>
internal sealed class ReconciliationJob(
    ReconciliationScanner scanner,
    JobLease lease,
    IOptions<ReconciliationOptions> options,
    ILogger<ReconciliationJob> logger) : ScheduledJob(lease, logger)
{
    protected override string Name => "wallet:reconciliation";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task RunAsync(CancellationToken ct)
    {
        var report = await scanner.ScanAsync(ct);

        if (report.IsClean)
        {
            logger.LogInformation("Mutabakat temiz: bulgu yok.");
            return;
        }

        // Projeksiyon ayrışması diğerlerinden AYRI seviyede. Diğer üçü dış dünyanın
        // gecikmesi — beklenen, yönetilebilir durumlar. Bu ise bizim kodumuzun
        // ledger'ı yanlış özetlediği anlamına geliyor ve her bakiye sorgusu o andan
        // itibaren yanlış cevap veriyor.
        foreach (var drift in report.Drifts)
        {
            logger.LogError(
                "Projeksiyon ledger ile tutmuyor. Hesap {AccountId}: bakiye {Projected}, " +
                "entry toplamı {FromEntries}, fark {Difference}",
                drift.LedgerAccountId, drift.Projected, drift.FromEntries, drift.Difference);
        }

        foreach (var item in report.AgingItems)
        {
            logger.LogWarning(
                "{Provider}: {Count} işlemin settlement'ı gelmedi, toplam beklenen ücret " +
                "{Amount}. En eskisi {Oldest}",
                item.Provider, item.Count, item.Amount, item.Oldest);
        }

        foreach (var invoice in report.PendingInvoices)
        {
            logger.LogWarning(
                "{Provider}/{InvoiceRef} incelemede bekliyor: fatura {Amount}, beklenen " +
                "{Expected}. Geliş {ReceivedAt}",
                invoice.Provider, invoice.InvoiceRef, invoice.Amount,
                invoice.Expected, invoice.ReceivedAt);
        }

        foreach (var overdue in report.OverdueFees)
        {
            logger.LogWarning(
                "{Provider}: {Count} ücret satırının faturası gecikti, beklenen toplam " +
                "{ExpectedTotal}. En eskisi {Oldest}",
                overdue.Provider, overdue.Count, overdue.ExpectedTotal, overdue.Oldest);
        }
    }
}
