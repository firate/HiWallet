using System.Text;
using HiWallet.BankIntegration.Persistence;
using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace HiWallet.BankAdapter.Infrastructure.Messaging;

/// <summary>
/// Kaydedilmiş ama wallet'a bildirilmemiş havaleleri broker'a taşır.
///
/// <b><c>bank_deposits</c> burada OUTBOX görevi görüyor</b>, <c>bank_transfers</c>
/// gibi: mesaj satırla birlikte yazıldı.
///
/// <b>Sıra: kaydet → yayınla → işaretle.</b> Ters sıra havaleyi kaybederdi: para
/// bankamızda kalır, wallet hiç duymazdı. Bu sırada en kötü ihtimalle aynı mesaj iki kez
/// gidiyor; wallet bankanın referansıyla deduplike ediyor.
///
/// <b>Kilit sıra için DEĞİL.</b> Havalenin cüzdanı yayın anında bilinmiyor ve wallet'ta
/// tek aktif tüketici var; kilit yalnızca aynı satırın iki instance tarafından
/// yayınlanmasını seyrekleştiriyor, asıl güvence <c>SKIP LOCKED</c>.
/// </summary>
internal sealed class DepositRelay(
    IDbContextFactory<BankDbContext> contextFactory,
    JobLease lease,
    RabbitMqConnection connection,
    MessagePublisher publisher,
    DepositTopology topology,
    TimeProvider timeProvider,
    ILogger<DepositRelay> logger) : BackgroundService
{
    private const string JobName = "bank:deposit-relay";

    private const int BatchSize = 50;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    private bool _declared;

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
                // Broker ya da DB erişilemez. Havaleler satırlarda duruyor, kayıp yok.
                logger.LogWarning(
                    exception, "Havale relay turu başarısız, {Delay} sonra yeniden denenecek.", ErrorDelay);

                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var pending = await db.Deposits
            .FromSql($"""
                      SELECT * FROM bank_deposits
                      WHERE published_at IS NULL
                      ORDER BY discovered_at
                      LIMIT {BatchSize}
                      FOR UPDATE SKIP LOCKED
                      """)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        await EnsureTopologyAsync(ct);

        var published = 0;

        foreach (var deposit in pending)
        {
            ct.ThrowIfCancellationRequested();

            deposit.PublishAttempts++;

            try
            {
                await publisher.PublishRawAsync(
                    topology.Exchange,
                    DepositTopology.RoutingKey,
                    Encoding.UTF8.GetBytes(deposit.Payload),
                    // Mesaj kimliği satırın kimliği: tekrar yayında aynı kalıyor.
                    messageId: deposit.Id,
                    type: DepositTopology.RoutingKey,
                    ct);

                deposit.PublishedAt = timeProvider.GetUtcNow();
                deposit.LastError = null;

                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                deposit.LastError = exception.Message;

                logger.LogWarning(
                    "Havale yayınlanamadı. {Provider}/{BankReference}, deneme {Attempt}: {Error}",
                    deposit.Provider, deposit.BankReference, deposit.PublishAttempts, exception.Message);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return published;
    }

    /// <summary>
    /// Kuyruğu burada da kuruyor: wallet-consumer'dan önce yayın yapılırsa exchange
    /// olmadığı için mesaj düşerdi. Declare idempotent.
    /// </summary>
    private async Task EnsureTopologyAsync(CancellationToken ct)
    {
        if (_declared) return;

        var current = await connection.GetAsync(ct);
        await using var channel = await current.CreateChannelAsync(cancellationToken: ct);

        await topology.DeclareAsync(channel, ct);
        _declared = true;
    }
}
