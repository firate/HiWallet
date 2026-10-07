using System.Text.Json;
using HiWallet.CardTopup.Application;
using HiWallet.CardTopup.Domain;
using HiWallet.Shared.Contracts.CardPayments;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.CardTopup.Infrastructure.Messaging;

/// <summary>
/// Sağlayıcının ödeme bildirimlerini dinler (topup-webhook'un relay'i yayınlıyor),
/// <see cref="ApplyCardPaymentHandler"/>'a verir. Yalnızca TAŞIMA: kanal, çözme, ack/nack.
///
/// Tek kanal, <c>prefetch=1</c>; kuyruk tek aktif tüketicili. Aynı yüklemeye tarama da
/// yazabiliyor; çakışmayı yüklemenin sürümü çözüyor.
/// </summary>
internal sealed class CardPaymentConsumer(
    RabbitMqConnection connection,
    CardPaymentTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<CardPaymentConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan RequeueDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Broker uygulamadan sonra ayağa kalkabilir; bağlanana kadar denenir.
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
                    exception, "Ödeme bildirimi tüketicisi başlatılamadı, 5 sn sonra yeniden denenecek.");

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

        await topology.DeclareAsync(channel, ct);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, ct);

        await channel.BasicConsumeAsync(topology.CardTopupQueue, autoAck: false, consumer, ct);

        _channel = channel;

        logger.LogInformation("Ödeme bildirimi tüketicisi {Queue} dinliyor.", topology.CardTopupQueue);
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        try
        {
            if (delivery.RoutingKey != CardPaymentTopology.RoutingKey)
            {
                throw new UnknownPaymentEventException(delivery.RoutingKey);
            }

            var message = JsonSerializer.Deserialize<CardPaymentUpdated>(delivery.Body.Span, JsonOptions)
                          ?? throw new JsonException("CardPaymentUpdated gövdesi boş.");

            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ApplyCardPaymentHandler>();

            var result = await handler.HandleAsync(message, ct);

            if (result is TransitionResult.Conflict)
            {
                // Yükleme değişmedi, alarm handler'da üretildi. Kuyruğa geri koymak aynı çelişkiyi
                // sonsuza kadar tekrarlardı.
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
                return;
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception exception) when (exception is JsonException or UnknownPaymentEventException
                                              or CardTopupNotFoundException)
        {
            // Kalıcı: aynı bayt aynı sonucu verir. Bizim açmadığımız ödemenin bildirimi de burada.
            logger.LogError(exception, "Ödeme bildirimi işlenemedi, dead-letter'a gidiyor.");

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Geçici olduğu varsayılan her şey (DB kapalı, süren çakışma).
            logger.LogWarning(exception, "Ödeme bildirimi işlenemedi, kuyruğa geri konuyor.");

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
