using System.Text;
using HiWallet.BankIntegration.Persistence;
using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.BankAdapter.Infrastructure.Messaging;

/// <summary>
/// Kapanmış ama cevabı yayınlanmamış transferleri broker'a taşır.
///
/// <b><c>bank_transfers</c> burada OUTBOX görevi görüyor.</b> Ayrı bir outbox
/// tablosu açılmadı: satır zaten "ne yaptık" kaydı ve cevabı da o taşıyor. İki
/// tablo olsaydı "sonucu öğrendim" ile "cevabı kuyruğa koydum" iki ayrı transaction'a
/// düşer ve aradaki boşlukta kaybolabilirdi.
///
/// <b>Sıra: kapat → yayınla → işaretle.</b> Ters sıra (önce işaretle, sonra yayınla)
/// cevabı kaybederdi ve saga sonsuza kadar beklerdi. Bu sırada en kötü ihtimalle
/// aynı cevap iki kez gidiyor — saga geçişleri idempotent, tekrarı <c>Ignored</c>
/// olarak yutuyor (decisions.md madde 31).
///
/// <b>Kilit VAR ama sıralama için değil.</b> Top-up relay'inde kilidin gerekçesi
/// aynı cüzdanın mesajlarının sırasıydı (madde 30); burada öyle bir kısıt yok —
/// her cevap ayrı bir saga'ya gidiyor. Kilit yalnızca aynı satırın iki instance
/// tarafından yayınlanmasını seyrekleştiriyor; asıl güvence <c>SKIP LOCKED</c>.
/// </summary>
internal sealed class ReplyRelay(
    IDbContextFactory<BankDbContext> contextFactory,
    JobLease lease,
    MessagePublisher publisher,
    WithdrawalTopology topology,
    TimeProvider timeProvider,
    ILogger<ReplyRelay> logger) : BackgroundService
{
    /// <summary>Kilit anahtarının kaynağı; bütün instance'larda AYNI olmak zorunda.</summary>
    private const string JobName = "bank:reply-relay";

    private const int BatchSize = 50;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = 0;

                var ran = await lease.TryRunAsync(
                    JobName,
                    async ct => published = await PublishBatchAsync(ct),
                    stoppingToken);

                if (!ran || published < BatchSize)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Broker ya da DB erişilemez. Cevaplar satırlarda duruyor, kayıp yok.
                logger.LogWarning(
                    exception, "Cevap relay turu başarısız, {Delay} sonra yeniden denenecek.", ErrorDelay);

                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED: iki instance aynı satırı almasın. Kilit bir şekilde
        // alınamadığında tek koruma bu kalıyor.
        var pending = await db.Transfers
            .FromSql($"""
                      SELECT * FROM bank_transfers
                      WHERE resolved_at IS NOT NULL AND reply_published_at IS NULL
                      ORDER BY resolved_at
                      LIMIT {BatchSize}
                      FOR UPDATE SKIP LOCKED
                      """)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var published = 0;

        foreach (var transfer in pending)
        {
            ct.ThrowIfCancellationRequested();

            transfer.PublishAttempts++;

            try
            {
                await publisher.PublishRawAsync(
                    topology.Exchange,
                    transfer.ReplyRoutingKey!,
                    Encoding.UTF8.GetBytes(transfer.ReplyPayload!),
                    // Mesaj kimliği komut kimliği: orchestrator tekrarı bununla
                    // eşleştiriyor. Yeni bir id üretmek tekrarı tanınmaz yapardı.
                    messageId: transfer.CommandId,
                    type: transfer.ReplyRoutingKey!,
                    ct);

                transfer.ReplyPublishedAt = timeProvider.GetUtcNow();
                transfer.LastError = null;

                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                transfer.LastError = exception.Message;

                logger.LogWarning(
                    "Cevap yayınlanamadı. Saga {SagaId}, deneme {Attempt}: {Error}",
                    transfer.SagaId, transfer.PublishAttempts, exception.Message);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return published;
    }
}
