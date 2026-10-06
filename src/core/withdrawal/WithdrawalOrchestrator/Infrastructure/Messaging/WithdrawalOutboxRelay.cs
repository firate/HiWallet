using System.Text;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Messaging;

/// <summary>
/// Outbox'ta yayınlanmamış komutları RabbitMQ'ya taşır (decisions.md madde 32).
/// Top-up'taki inbox relay'iyle aynı kalıp, ters yön.
///
/// <b>En az bir kez teslim.</b> Publish başarılı olup commit'ten önce süreç ölürse
/// satır "yayınlanmamış" kalıyor ve komut ikinci kez gidiyor. Bunu kapatmanın yolu
/// yok — broker ile veritabanı arasında ortak transaction olamaz. Çözüm alıcı
/// tarafta: <c>CommandId</c> + <c>processed_messages</c> ikinci teslimi yutuyor.
/// Ters sırada (önce işaretle, sonra publish) hata KAYIP olurdu ve kaybedilen şey
/// bir komut — saga sonsuza kadar bekler.
///
/// <b>Gövde yeniden serialize EDİLMİYOR.</b> Payload outbox'a yazılırken bir kez
/// üretildi; buradaki iş onu baytlara çevirip göndermek. Yeniden dönüştürülseydi
/// commit'ten sonra ortaya çıkan bir serialize hatası mümkün olurdu.
/// </summary>
internal sealed class WithdrawalOutboxRelay(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    RabbitMqConnection connection,
    WithdrawalTopology topology,
    TimeProvider timeProvider,
    ILogger<WithdrawalOutboxRelay> logger) : BackgroundService
{
    private const int BatchSize = 50;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishBatchAsync(stoppingToken);

                if (published < BatchSize)
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
                // Broker ya da DB erişilemez. Satırlar outbox'ta duruyor, kayıp yok.
                logger.LogWarning(
                    exception, "Outbox turu başarısız, {Delay} sonra yeniden denenecek.", ErrorDelay);

                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED: çok instance'ta iki relay aynı satırı almasın
        // (decisions.md madde 3). Top-up'taki sıralama sorunu (madde 30) burada YOK:
        // bir saga'nın aynı anda birden fazla bekleyen komutu olamıyor, çünkü her
        // geçiş en fazla bir komut üretiyor ve bir sonraki ancak cevabı gelince
        // yazılıyor.
        var batch = await db.Outbox
            .FromSql(
                $"""
                 SELECT * FROM withdrawal_outbox
                 WHERE published_at IS NULL
                 ORDER BY created_at
                 LIMIT {BatchSize}
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        var channel = await GetChannelAsync(ct);
        var now = timeProvider.GetUtcNow();
        var published = 0;

        foreach (var message in batch)
        {
            message.PublishAttempts++;

            try
            {
                await PublishAsync(channel, message, ct);

                message.PublishedAt = now;
                message.LastError = null;
                published++;
            }
            catch (Exception exception)
            {
                // Tek satırın hatası batch'in kalanını durdurmuyor.
                message.LastError = exception.Message;

                logger.LogWarning(
                    exception, "Outbox satırı yayınlanamadı. {RoutingKey}, saga {SagaId}, deneme {Attempts}",
                    message.RoutingKey, message.SagaId, message.PublishAttempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return published;
    }

    private async Task PublishAsync(IChannel channel, OutboxMessage message, CancellationToken ct)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",

            // Alıcı tarafındaki deduplikasyonun anahtarı. Satır kimliği ile komuttaki
            // CommandId aynı değer (OutboxMessage.For), o yüzden burada dönüşüm yok.
            MessageId = message.Id.ToString(),
            Type = message.RoutingKey,
            Timestamp = new AmqpTimestamp(message.CreatedAt.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: topology.Exchange,
            routingKey: message.RoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(message.Payload),
            cancellationToken: ct);
    }

    /// <summary>
    /// Publisher confirms AÇIK: onaysız publish "gönderdim" der ama broker'ın
    /// yazdığını garanti etmez, kayıp sessiz olur.
    /// </summary>
    private async ValueTask<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        if (_channel is not null) await _channel.DisposeAsync();

        var current = await connection.GetAsync(ct);

        _channel = await current.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            ct);

        // mandatory tek başına yetmiyor: yönlendirilemeyen mesaj istisna fırlatmıyor,
        // geri dönüyor. Beklenen bir durum DEĞİL — çıkarsa topoloji bozuk demektir.
        _channel.BasicReturnAsync += (_, returned) =>
        {
            logger.LogError(
                "Komut hiçbir kuyruğa yönlendirilemedi: {ReplyText}. Routing key {RoutingKey}",
                returned.ReplyText, returned.RoutingKey);

            return Task.CompletedTask;
        };

        await topology.DeclareAsync(_channel, ct);

        return _channel;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }
    }
}
