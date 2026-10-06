using System.Text;
using HiWallet.CardTopup.Infrastructure.Persistence;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace HiWallet.CardTopup.Infrastructure.Messaging;

/// <summary>
/// Outbox'ta yayınlanmamış kapanışları wallet'a taşır. Çekim orchestrator'ının relay'iyle aynı
/// kalıp: önce publish, sonra işaretle; en az bir kez teslim, tekrarı wallet'taki kapanış
/// satırı yutuyor. Ters sıra kayıp üretirdi ve kaybolan şey bir payın kapanışı olurdu.
///
/// <b>Kilitlenmiyor</b> (top-up relay'inin aksine): bir yüklemenin tek kapanışı var. Geç
/// yazılan payın kapanışı ikinci bir satır ama sırası önemsiz; ikisi de ödenmedi.
/// </summary>
internal sealed class CardTopupOutboxRelay(
    IDbContextFactory<CardTopupDbContext> contextFactory,
    RabbitMqConnection connection,
    CardTopupTopology topology,
    TimeProvider timeProvider,
    ILogger<CardTopupOutboxRelay> logger) : BackgroundService
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

        // SKIP LOCKED: çok instance'ta iki relay aynı satırı almasın.
        var batch = await db.Outbox
            .FromSql(
                $"""
                 SELECT * FROM card_topup_outbox
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
                    exception, "Kapanış yayınlanamadı. Yükleme {CardTopupId}, deneme {Attempts}",
                    message.CardTopupId, message.PublishAttempts);
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
            MessageId = message.Id.ToString(),
            Type = CardTopupTopology.RoutingKey,
            Timestamp = new AmqpTimestamp(message.CreatedAt.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: topology.Exchange,
            routingKey: CardTopupTopology.RoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(message.Payload),
            cancellationToken: ct);
    }

    /// <summary>Publisher confirms AÇIK: onaysız publish kaybı sessiz bırakırdı.</summary>
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

        _channel.BasicReturnAsync += (_, returned) =>
        {
            logger.LogError(
                "Kapanış hiçbir kuyruğa yönlendirilemedi: {ReplyText}. Routing key {RoutingKey}",
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
