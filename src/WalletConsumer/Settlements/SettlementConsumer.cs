using System.Text.Json;
using HiWallet.Shared.Contracts.Settlements;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletService.Application.Settlements;
using HiWallet.WalletService.Domain.Policies;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.WalletConsumer.Settlements;

/// <summary>
/// Sağlayıcı settlement bildirimlerini dinler ve ledger'a yazdırır (adım 5.5).
///
/// <b>Cevap yayınlamıyor.</b> Çekim komutlarının aksine burada karşı taraf yok:
/// settlement tek yönlü bir bildirim. O yüzden outbox da yok, ack yeterli.
///
/// <b>prefetch=1.</b> İki settlement aynı anda işlenirse ikisi de aynı
/// <c>clearing</c> ve <c>nostro</c> bakiye satırlarına yazar ve optimistic lock
/// birini reddeder. Reddedilen yeniden denenir, yani doğruluk zaten korunuyor —
/// ama sıraya sokmak boşa çakışma üretmemek için ucuz bir tercih. Settlement hacmi
/// batch olduğu için verim sorunu değil.
///
/// <b>Bu sınıf yalnızca TAŞIMA.</b> Ledger'a yazan iş kuralı
/// <c>WalletService.Core</c>'da (madde 25).
/// </summary>
internal sealed class SettlementConsumer(
    RabbitMqConnection connection,
    SettlementTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<SettlementConsumer> logger) : BackgroundService
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
                    exception, "Settlement tüketicisi başlatılamadı, 5 sn sonra yeniden denenecek.");

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

        // Declare idempotent; topup-webhook önce kurmuşsa bu çağrı doğrulama yapıyor.
        await topology.DeclareAsync(channel, ct);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, ct);

        await channel.BasicConsumeAsync(topology.WalletQueue, autoAck: false, consumer, ct);

        _channel = channel;

        logger.LogInformation("Settlement tüketicisi {Queue} dinliyor.", topology.WalletQueue);
    }

    private async Task OnMessageAsync(
        IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        try
        {
            var message = JsonSerializer.Deserialize<SettlementReceived>(delivery.Body.Span, JsonOptions)
                          ?? throw new JsonException("Settlement gövdesi boş.");

            using var scope = scopeFactory.CreateScope();

            var result = await scope.ServiceProvider
                .GetRequiredService<ProcessSettlementHandler>()
                .HandleAsync(message, ct);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);

            if (result.Replayed)
            {
                logger.LogInformation(
                    "Settlement tekrar teslim edildi; ledger'a dokunulmadı. {Provider}/{SettlementId}",
                    message.Provider, message.SettlementId);
            }
        }
        catch (Exception exception)
            when (exception is JsonException or SettlementRejectedException or UnknownProviderException)
        {
            // KALICI hata: bozuk gövde, tutarsız batch, eksik sistem hesabı, tanımsız
            // sağlayıcı. Aynı bayt aynı sonucu verir; requeue etmek kuyruğu süresiz
            // tıkardı (CLAUDE.md "Top-up hattı").
            //
            // Top-up'takinden farkı: orada tanımsız sağlayıcı müşterinin parasını
            // ilgilendiriyordu ve kendi konfigürasyon hatamız yüzünden mesajı çöpe
            // atmıyorduk. Burada para zaten bankaya girmiş; dead-letter'daki satır
            // incelenip elle işlenebiliyor ve mutabakat farkı zaten raporlayacak.
            logger.LogError(
                exception, "Settlement işlenemedi, dead-letter'a gidiyor.");

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // GEÇİCİ varsayılan her şey: DB kapalı, timeout, optimistic lock çakışması.
            logger.LogWarning(exception, "Settlement işlenemedi, kuyruğa geri konuyor.");

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
