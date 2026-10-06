using System.Text.Json;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Messaging;

/// <summary>
/// Wallet ve bank-service'in event'lerini dinler, <see cref="AdvanceSagaHandler"/>'a
/// verir.
///
/// <b>Bu sınıf yalnızca TAŞIMA.</b> Kanal açmak, mesajı tipine göre çözmek, ack/nack
/// kararı. Geçiş kuralları state machine'de, kalıcılık handler'da.
///
/// <b>Tek kanal, <c>prefetch=1</c>.</b> Top-up'taki gibi partition YOK: oradaki amaç
/// aynı cüzdanın mesajlarını sıralı tutarken farklı cüzdanları paralel akıtmaktı.
/// Burada sıra zaten saga'nın nedenselliğinden geliyor ve hacim düşük. Paralelliğe
/// ihtiyaç olursa prefetch artırılır — aynı saga'ya iki mesaj denk gelirse
/// <c>withdrawal_sagas.version</c> ikincisini reddediyor ve requeue ediliyor.
/// </summary>
internal sealed class WithdrawalEventConsumer(
    RabbitMqConnection connection,
    WithdrawalTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<WithdrawalEventConsumer> logger) : BackgroundService
{
    /// <summary>Geçici hatada mesaj kuyruğa geri konmadan önce beklenen süre.</summary>
    private static readonly TimeSpan RequeueDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Broker uygulamadan sonra ayağa kalkabilir; bağlanana kadar denenir.
        // Başlangıçta patlamak container'ı restart döngüsüne sokardı.
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
                    exception, "Saga event tüketicisi başlatılamadı, 5 sn sonra yeniden denenecek.");

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

        // Declare idempotent; relay önce kurmuşsa bu çağrı yalnızca doğrulama yapıyor.
        await topology.DeclareAsync(channel, ct);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, ct);

        await channel.BasicConsumeAsync(topology.OrchestratorQueue, autoAck: false, consumer, ct);

        _channel = channel;

        logger.LogInformation("Saga event tüketicisi {Queue} dinliyor.", topology.OrchestratorQueue);
    }

    private async Task OnMessageAsync(
        IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        // Routing key = mesaj tipinin adı (WithdrawalTopology). Gövdeye bakmadan
        // hangi event olduğu buradan biliniyor.
        var eventName = delivery.RoutingKey;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<AdvanceSagaHandler>();

            var result = await DispatchAsync(handler, eventName, delivery.Body.Span, ct);

            if (result is TransitionResult.Conflict)
            {
                // Saga durumu değişmedi ve alarm handler'da üretildi. Kuyruğa geri
                // koymak aynı çelişkiyi sonsuza kadar tekrarlardı; dead-letter mesajı
                // inceleme için saklıyor.
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
                return;
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception exception) when (exception is JsonException or UnknownEventException or SagaNotFoundException)
        {
            // Kalıcı hata: aynı bayt aynı sonucu verir, yeniden denemek kuyruğu tıkar.
            logger.LogError(
                exception, "Saga event'i işlenemedi, dead-letter'a gidiyor. {Event}", eventName);

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Kapanıyoruz. Mesaj ack'lenmedi, sonraki açılışta yeniden teslim edilecek.
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Aynı saga'ya başka bir yazar yetişti. Geçici — yeni durumla yeniden
            // değerlendirilmeli, o yüzden requeue.
            logger.LogWarning("Saga çakışması, mesaj kuyruğa geri konuyor. {Event}", eventName);

            await Task.Delay(RequeueDelay, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
        catch (Exception exception)
        {
            // Geçici olduğu varsayılan her şey (DB kapalı, deadlock, timeout).
            // Gecikme olmadan requeue edilseydi aynı mesaj sıcak döngüye girerdi.
            logger.LogWarning(
                exception, "Saga event'i işlenemedi, kuyruğa geri konuyor. {Event}", eventName);

            await Task.Delay(RequeueDelay, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
    }

    /// <summary>
    /// Tanınmayan bir routing key sessizce ack'lenmiyor: sözleşme değişmiş ve bu
    /// servis eski sürümde olabilir. Dead-letter mesajı saklıyor.
    /// </summary>
    private static Task<TransitionResult> DispatchAsync(
        AdvanceSagaHandler handler, string eventName, ReadOnlySpan<byte> body, CancellationToken ct)
    {
        return eventName switch
        {
            nameof(WithdrawalDebited) => handler.HandleAsync(Read<WithdrawalDebited>(body), ct),
            nameof(WithdrawalDebitRejected) => handler.HandleAsync(Read<WithdrawalDebitRejected>(body), ct),
            nameof(BankTransferSucceeded) => handler.HandleAsync(Read<BankTransferSucceeded>(body), ct),
            nameof(BankTransferFailed) => handler.HandleAsync(Read<BankTransferFailed>(body), ct),
            nameof(WithdrawalSettled) => handler.HandleAsync(Read<WithdrawalSettled>(body), ct),
            nameof(WithdrawalRefunded) => handler.HandleAsync(Read<WithdrawalRefunded>(body), ct),
            _ => throw new UnknownEventException(eventName)
        };
    }

    private static T Read<T>(ReadOnlySpan<byte> body) =>
        JsonSerializer.Deserialize<T>(body, JsonOptions)
        ?? throw new JsonException($"{typeof(T).Name} gövdesi boş.");

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_channel is not null)
        {
            // Yalnızca DisposeAsync: kanalı zaten kapatıyor. Öncesinde CloseAsync
            // çağırmak çift temizlik demek ve otomatik kurtarma disposed kanalın
            // consumer'larını okumaya çalışıp ObjectDisposedException fırlatıyor.
            await _channel.DisposeAsync();
            _channel = null;
        }
    }
}

internal sealed class UnknownEventException(string eventName)
    : Exception($"Tanınmayan saga event'i: {eventName}");
