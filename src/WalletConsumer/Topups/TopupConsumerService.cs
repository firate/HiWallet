using System.Text.Json;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletService.Application.Topups;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.WalletConsumer.Topups;

/// <summary>
/// Top-up kuyruklarını dinler ve <see cref="ProcessTopupHandler"/>'a verir.
///
/// <b>Bu sınıf yalnızca TAŞIMA.</b> Kanal açmak, ack/nack kararı, dead-letter'a
/// yollamak. Ledger'a yazan iş kuralı <c>WalletService.Core</c>'da ve wallet-api ile
/// paylaşılıyor — bu uygulama onun ikinci bir kopyasını taşımıyor
/// (decisions.md madde 25).
///
/// <b>Partition başına ayrı kanal.</b> Tek kanalda dört kuyruk dinlense mesajlar
/// sırayla işlenirdi (kanal başına dispatch tek iş parçacığı); partition'ların amacı
/// tam tersi — farklı cüzdanlar paralel aksın. Kanal başına <c>prefetch=1</c> ise
/// aynı cüzdanın mesajlarının sırasını koruyor (overview.md madde 8).
/// </summary>
internal sealed class TopupConsumerService(
    RabbitMqConnection connection,
    TopupTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<TopupConsumerService> logger) : BackgroundService
{
    /// <summary>Geçici hatada mesaj kuyruğa geri konmadan önce beklenen süre.</summary>
    private static readonly TimeSpan RequeueDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Kilit altında. <c>BackgroundService.StopAsync</c>, iptal token'ı zaten
    /// tetiklenmişse <c>ExecuteAsync</c>'i BEKLEMEDEN dönüyor — yani abone olma
    /// döngüsü hâlâ kanal eklerken temizlik başlayabiliyor.
    /// </summary>
    private readonly List<IChannel> _channels = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var partitionCount = topology.PartitionCount;

        // Broker uygulamadan sonra ayağa kalkabilir; bağlanana kadar denenir.
        // Başlangıçta patlamak container'ı restart döngüsüne sokardı.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(partitionCount, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception, "Top-up tüketicisi başlatılamadı, 5 sn sonra yeniden denenecek.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task StartConsumingAsync(int partitionCount, CancellationToken ct)
    {
        var current = await connection.GetAsync(ct);

        // İlk kanal topolojiyi kuruyor; declare idempotent, webhook tarafı önce
        // kurmuşsa bu çağrı sadece doğrulama yapıyor.
        await using (var setup = await current.CreateChannelAsync(cancellationToken: ct))
        {
            await topology.DeclareAsync(setup, ct);
        }

        for (var partition = 0; partition < partitionCount; partition++)
        {
            var queue = topology.PartitionQueue(partition);

            var channel = await current.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: false,
                    publisherConfirmationTrackingEnabled: false,
                    consumerDispatchConcurrency: 1),
                ct);

            // prefetch=1: bir mesaj ack'lenmeden sonraki gönderilmiyor. Sıralamanın
            // korunması buna bağlı — daha yüksek bir değer aynı cüzdanın iki mesajını
            // aynı anda işleme sokardı.
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, queue, delivery, ct);

            await channel.BasicConsumeAsync(queue, autoAck: false, consumer, ct);

            lock (_channels)
            {
                _channels.Add(channel);
            }
        }

        logger.LogInformation("Top-up tüketicisi {PartitionCount} partition dinliyor.", partitionCount);
    }

    private async Task OnMessageAsync(
        IChannel channel, string queue, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        TopupReceived? message;

        try
        {
            message = JsonSerializer.Deserialize<TopupReceived>(delivery.Body.Span, JsonOptions);
        }
        catch (JsonException exception)
        {
            // Gövde bozuk. Kuyruğa geri koymanın anlamı yok, aynı bayt aynı hatayı verir.
            logger.LogError(exception, "Top-up mesajı çözümlenemedi, dead-letter'a gidiyor. Kuyruk {Queue}", queue);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        if (message is null)
        {
            logger.LogError("Top-up mesajı boş, dead-letter'a gidiyor. Kuyruk {Queue}", queue);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ProcessTopupHandler>();

            await handler.HandleAsync(message, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (TopupRejectedException exception)
        {
            // Kalıcı hata: yeniden denemek aynı sonucu verir ve partition'ı tıkar.
            logger.LogError(exception, "Top-up reddedildi, dead-letter'a gidiyor.");
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Kapanıyoruz. Mesaj ack'lenmedi, kuyrukta kalıyor ve sonraki açılışta
            // yeniden teslim edilecek.
            throw;
        }
        catch (Exception exception)
        {
            // Geçici olduğu varsayılan her şey (DB kapalı, deadlock, timeout).
            // Gecikme olmadan requeue edilseydi aynı mesaj sıcak döngüye girerdi.
            logger.LogWarning(
                exception, "Top-up işlenemedi, kuyruğa geri konuyor. {Provider}/{EventId}",
                message.Provider, message.EventId);

            await Task.Delay(RequeueDelay, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        IChannel[] channels;

        lock (_channels)
        {
            channels = [.. _channels];
            _channels.Clear();
        }

        foreach (var channel in channels)
        {
            // Yalnızca DisposeAsync: kanalı zaten kapatıyor. Öncesinde CloseAsync
            // çağırmak çift temizlik demek ve otomatik kurtarma ikinci turda
            // disposed kanalın consumer'larını okumaya çalışıp
            // ObjectDisposedException fırlatıyor.
            await channel.DisposeAsync();
        }
    }
}
