using System.Text.Json;
using HiWallet.Shared.Contracts.CardTopups;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletService.Application.CardTopups;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.WalletConsumer.CardTopups;

/// <summary>
/// Kartla yüklemenin kapanışlarını dinler ve <see cref="ProcessCardTopupHandler"/>'a verir:
/// ödendiyse para cüzdana, ödenmediyse pay serbest.
///
/// <b>Bu sınıf yalnızca TAŞIMA.</b> Ledger kaydı <c>WalletService.Core</c>'da (decisions.md
/// madde 25).
///
/// Kalıcı hata (pay yok, tutar uyuşmuyor, ödenmedi diye kapanmış yüklemenin parası geldi)
/// dead-letter ve ALARM: iki servis aynı ödeme için farklı şey biliyor. Geçici hata (DB
/// kapalı) kuyruğa geri. İkisi karışırsa tek aktif tüketicili kuyruk süresiz tıkanır.
/// </summary>
internal sealed class CardTopupConsumer(
    RabbitMqConnection connection,
    CardTopupTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<CardTopupConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan RequeueDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception, "Kartla yükleme tüketicisi başlatılamadı, 5 sn sonra yeniden denenecek.");

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken ct)
    {
        var current = await connection.GetAsync(ct);

        var channel = await current.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: 1),
            ct);

        // Declare idempotent; card-topup önce kurmuşsa bu çağrı doğrulama yapıyor.
        await topology.DeclareAsync(channel, ct);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, ct);

        await channel.BasicConsumeAsync(topology.WalletQueue, autoAck: false, consumer, ct);

        _channel = channel;

        logger.LogInformation("Kartla yükleme tüketicisi {Queue} dinliyor.", topology.WalletQueue);
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        try
        {
            var message = JsonSerializer.Deserialize<CardTopupClosed>(delivery.Body.Span, JsonOptions)
                          ?? throw new JsonException("Kapanış gövdesi boş.");

            using var scope = scopeFactory.CreateScope();

            await scope.ServiceProvider.GetRequiredService<ProcessCardTopupHandler>().HandleAsync(message, ct);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception exception) when (exception is JsonException or CardTopupRejectedException)
        {
            logger.LogCritical(
                exception, "Kartla yükleme kapanışı işlenemiyor, dead-letter'a gidiyor. Kart yüklemesi servisi ile wallet ayrıştı.");

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // GEÇİCİ varsayılan her şey: DB kapalı, çakışma.
            logger.LogWarning(exception, "Kartla yükleme kapanışı işlenemedi, kuyruğa geri konuyor.");

            await Task.Delay(RequeueDelay, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
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
