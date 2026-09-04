using System.Text;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.TopupWebhook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.TopupWebhook.Infrastructure.Messaging;

/// <summary>
/// Inbox'ta yayınlanmamış satırları RabbitMQ'ya taşır (overview.md madde 5).
///
/// <b>En az bir kez teslim.</b> Publish başarılı olup commit'ten önce süreç ölürse
/// satır "yayınlanmamış" kalıyor ve mesaj ikinci kez gidiyor. Bunu kapatmanın yolu
/// yok — broker ile veritabanı arasında ortak bir transaction olamaz. Çözüm
/// tüketicide: <c>processed_events</c> ikinci teslimi yutuyor. Ters sırada
/// (önce işaretle, sonra publish) hata KAYIP olurdu; kaybetmektense iki kez
/// göndermek tercih ediliyor.
///
/// <b><c>FOR UPDATE SKIP LOCKED</c>.</b> Çok instance'ta iki relay aynı satırı
/// almasın diye (decisions.md madde 3). Advisory lock ile tek instance'a indirmek
/// de mümkündü ama gereksiz: SKIP LOCKED instance'ları paralel çalıştırıyor.
/// </summary>
internal sealed class TopupRelay(
    IDbContextFactory<InboxDbContext> contextFactory,
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<TopupRelay> logger) : BackgroundService
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

                // Dolu batch geldiyse hemen devam: birikmiş kuyruğu boşaltırken
                // yarım saniye beklemenin anlamı yok.
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
                // Broker ya da DB erişilemez. Satırlar inbox'ta duruyor, kayıp yok.
                logger.LogWarning(exception, "Relay turu başarısız, {Delay} sonra yeniden denenecek.", ErrorDelay);
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var batch = await db.Inbox
            .FromSql(
                $"""
                 SELECT * FROM topup_inbox
                 WHERE published_at IS NULL
                 ORDER BY received_at
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
                // Tek satırın hatası batch'in kalanını durdurmuyor. Satır
                // yayınlanmamış kalıyor, sonraki turda tekrar denenecek.
                message.LastError = exception.Message;

                logger.LogWarning(
                    exception, "Inbox satırı yayınlanamadı. {Provider}/{EventId}, deneme {Attempts}",
                    message.Provider, message.EventId, message.PublishAttempts);
            }
        }

        // Publish ile commit arasındaki satırlar kilitli: relay'in DB transaction'ı
        // ağ turlarını kapsıyor. Batch küçük tutuluyor ki kilit süresi kısa kalsın.
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return published;
    }

    private static async Task PublishAsync(IChannel channel, InboxMessage message, CancellationToken ct)
    {
        var properties = new BasicProperties
        {
            // Broker yeniden başlarsa mesaj hayatta kalsın.
            Persistent = true,
            ContentType = "application/json",
            MessageId = $"{message.Provider}:{message.EventId}",
            Type = nameof(Shared.Contracts.Topups.TopupReceived),
            Timestamp = new AmqpTimestamp(message.ReceivedAt.ToUnixTimeSeconds())
        };

        // Routing key = cüzdan id. Consistent hash exchange bunu hash'leyip
        // partition seçiyor; aynı cüzdan hep aynı kuyruğa (overview.md madde 8).
        await channel.BasicPublishAsync(
            exchange: TopupTopology.Exchange,
            routingKey: message.LedgerAccountId.ToString(),
            mandatory: true,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(message.Payload),
            cancellationToken: ct);
    }

    /// <summary>
    /// Publisher confirms AÇIK: onaysız publish "gönderdim" der ama broker'ın
    /// yazdığını garanti etmez, kayıp sessiz olur. Açıkken <c>BasicPublishAsync</c>
    /// onayı bekliyor ve reddedilirse istisna atıyor.
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

        // Routing yapılamayan mesaj sessizce düşer; mandatory + bu olay onu
        // görünür kılıyor. Beklenen durum DEĞİL — çıkarsa topoloji bozuk demektir.
        _channel.BasicReturnAsync += (_, returned) =>
        {
            logger.LogError(
                "Mesaj hiçbir kuyruğa yönlendirilemedi: {ReplyText}. Routing key {RoutingKey}",
                returned.ReplyText, returned.RoutingKey);

            return Task.CompletedTask;
        };

        await TopupTopology.DeclareAsync(_channel, options.Value.PartitionCount, ct);

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
