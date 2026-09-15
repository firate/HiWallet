using HiWallet.BankAdapter.Application;
using HiWallet.BankIntegration.Domain;
using HiWallet.BankIntegration.Persistence;
using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HiWallet.BankAdapter.Infrastructure.Jobs;

/// <summary>
/// Uzun süredir cevapsız kalan transferleri bankaya sorup kapatır.
///
/// <b>İkinci bir teslim kanalı DEĞİL, bir KONTROL</b> (decisions.md madde 35).
/// Sonuçların neredeyse tamamı callback'le geliyor; bu tarama yalnızca kaçırılanı
/// topluyor ve günde birkaç kez koşuyor.
///
/// <b>Varlığı zorunlu.</b> Webhook teslimi garanti değil ve banka sınırlı sayıda
/// deneyip vazgeçiyor. Bu tarama olmasaydı kaçırılan bir callback kalıcı kayıp
/// olurdu: satır <c>pending</c> kalır, saga <c>bank_transfer_pending</c>'de asılır
/// ve müşterinin parası clearing'de durur.
///
/// <b>Bulduğu satır sayısı doğrudan bir alarm sinyali.</b> Callback hattı sağlıklıyken
/// bu tarama boş dönüyor; dolu dönmeye başladığı an webhook tarafında bir sorun var
/// demektir. Ayrı bir sağlık kontrolü yazmaya gerek kalmıyor.
///
/// Bu, orchestrator'daki takılmış saga taramasıyla (madde 33) aynı desen — ama aynı
/// şey DEĞİL: o iki veritabanı arasındaki ayrışmaya bakıyor, bu bankayla bizim
/// aramızdakine.
/// </summary>
internal sealed class ReconciliationScan(
    IDbContextFactory<BankDbContext> contextFactory,
    BankClient bank,
    TransferCompleter completer,
    IOptions<BankAdapterOptions> options,
    JobLease lease,
    TimeProvider timeProvider,
    ILogger<ReconciliationScan> logger) : ScheduledJob(lease, logger)
{
    private readonly ReconciliationOptions _options = options.Value.Reconciliation;

    protected override string Name => "bank:reconciliation";

    protected override TimeSpan Interval => _options.Interval;

    protected override async Task RunAsync(CancellationToken ct)
    {
        var cutoff = timeProvider.GetUtcNow() - _options.StaleAfter;

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // StaleAfter filtresi taramanın kapsamını daraltıyor: her bekleyen transfer
        // değil, yalnızca bu süreden uzundur cevapsız kalanlar. Mutlu yolda burası
        // boş dönüyor.
        //
        // bank_reference NULL olanlar dışarıda: banka kabul etmemiş bir transferin
        // soracak referansı yok. Böyle bir satır oluşmaması gerekiyor — oluştuysa
        // sorun burada değil, kayıt yolunda.
        var stale = await db.Transfers
            .AsNoTracking()
            .Where(t => t.ResolvedAt == null
                        && t.BankReference != null
                        && t.StartedAt <= cutoff)
            .OrderBy(t => t.StartedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            logger.LogDebug("Mutabakat taraması: cevapsız transfer yok.");
            return;
        }

        logger.LogWarning(
            "Mutabakat taraması {Count} cevapsız transfer buldu ({StaleAfter} üzeri). " +
            "Sıfırdan farklı olması callback hattında sorun olabileceğini gösterir.",
            stale.Count, _options.StaleAfter);

        var resolved = 0;

        foreach (var transfer in stale)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (await ReconcileAsync(transfer, ct)) resolved++;
            }
            catch (TransientBankException exception)
            {
                // Banka şu an cevap veremiyor. Tur yarıda kesiliyor: kalan
                // transferleri de sormanın anlamı yok, hepsi aynı hatayı alacak.
                logger.LogWarning(
                    "Mutabakat turu yarıda kesildi, banka cevap vermiyor: {Message}", exception.Message);

                break;
            }
        }

        logger.LogInformation(
            "Mutabakat taraması bitti: {Resolved}/{Total} transfer kapatıldı.", resolved, stale.Count);
    }

    private async Task<bool> ReconcileAsync(BankTransfer transfer, CancellationToken ct)
    {
        var status = await bank.GetStatusAsync(transfer.BankReference!, ct);

        if (status is null)
        {
            // Banka referansı TANIMIYOR. Transferi başarısız SAYMIYORUZ: banka
            // gerçekten göndermiş ama kaydı başka bir yerde olabilir. Uydurulmuş
            // bir "başarısız" kararı müşterinin parasını geri verir ve para hem
            // müşteride hem bankadan çıkmış olur.
            logger.LogError(
                "Banka referansı tanımıyor: {BankReference}, saga {SagaId}. Elle bakılmalı.",
                transfer.BankReference, transfer.SagaId);

            return false;
        }

        var mapped = BankClient.Map(status.Status);

        if (mapped is null)
        {
            logger.LogError(
                "Bankanın bilinmeyen durumu: '{Status}' ({BankReference}). Sözleşme değişmiş olabilir.",
                status.Status, transfer.BankReference);

            return false;
        }

        if (mapped is BankTransferStatus.Pending)
        {
            // Banka da "hâlâ işliyorum" diyor. Beklemeye devam — bu bir hata değil,
            // yalnızca uzun süren bir transfer.
            logger.LogInformation(
                "Banka hâlâ işliyor. {BankReference}, {Elapsed} önce başladı.",
                transfer.BankReference, timeProvider.GetUtcNow() - transfer.StartedAt);

            return false;
        }

        return await completer.ResolveAsync(
            transfer.BankReference!,
            mapped.Value,
            status.Fee,
            status.FailureReason,
            TransferCompleter.ViaReconciliation,
            ct);
    }
}
