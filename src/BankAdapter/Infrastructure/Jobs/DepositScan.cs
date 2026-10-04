using HiWallet.BankAdapter.Application;
using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.Extensions.Options;

namespace HiWallet.BankAdapter.Infrastructure.Jobs;

/// <summary>
/// Bankanın hesap hareketlerinden bildirimi kaçırılmış havaleleri bulup kaydeder.
///
/// <b>İkinci bir teslim kanalı DEĞİL, bir KONTROL</b> (decisions.md madde 35), transferlerin
/// mutabakat taraması gibi. Havalelerin neredeyse tamamı bildirimle geliyor; bu tarama
/// kaçırılanı topluyor. Bulduğu her havale bir alarm sinyali.
///
/// <b>Varlığı zorunlu.</b> Banka bildirimi sınırlı sayıda deneyip vazgeçiyor. Bu tarama
/// olmasaydı kaçırılan bir bildirim müşterinin parasını bankada bırakırdı: hesabımızda
/// ama ne cüzdanda ne askıda.
///
/// Zaman penceresi: <c>Lookback</c> öncesinden <c>StaleAfter</c> öncesine kadar. Daha yeni
/// havalelerin bildirimi yolda olabilir; onlar bir sonraki turda.
/// </summary>
internal sealed class DepositScan(
    BankClient bank,
    DepositRecorder deposits,
    IOptions<BankAdapterOptions> options,
    JobLease lease,
    TimeProvider timeProvider,
    ILogger<DepositScan> logger) : ScheduledJob(lease, logger)
{
    private readonly BankAdapterOptions _options = options.Value;

    protected override string Name => "bank:deposit-reconciliation";

    protected override TimeSpan Interval => _options.DepositReconciliation.Interval;

    protected override Task RunAsync(CancellationToken ct) => ScanAsync(ct);

    /// <returns>Bildirimi kaçırılmış ve bu turda kaydedilen havale sayısı.</returns>
    internal async Task<int> ScanAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var window = _options.DepositReconciliation;

        var incoming = await bank.GetIncomingTransfersAsync(now - window.Lookback, now - window.StaleAfter, ct);

        var found = 0;

        foreach (var transfer in incoming)
        {
            ct.ThrowIfCancellationRequested();

            if (await deposits.RecordAsync(_options.Provider!, transfer, DepositRecorder.ViaReconciliation, ct))
            {
                found++;
            }
        }

        if (found > 0)
        {
            logger.LogWarning(
                "Hesap hareketi taraması bildirimi kaçırılmış {Found} havale buldu ({Total} hareket içinde). " +
                "Sıfırdan farklı olması bildirim hattında sorun olabileceğini gösterir.",
                found, incoming.Count);
        }
        else
        {
            logger.LogDebug("Hesap hareketi taraması: kaçırılmış havale yok ({Total} hareket).", incoming.Count);
        }

        return found;
    }
}
