using System.Text.Json;
using HiWallet.Shared.Contracts.Deposits;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletService.Application.Deposits;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.WalletConsumer.Deposits;

/// <summary>
/// Gelen havaleleri dinler ve <see cref="ProcessDepositHandler"/>'a verir.
///
/// <b>Bu sınıf yalnızca TAŞIMA.</b> Kararın kendisi (cüzdan mı askı mı) ve ledger kaydı
/// <c>WalletService.Core</c>'da (decisions.md madde 25).
///
/// <b>Askı bir hata DEĞİL.</b> Sahibi belirlenemeyen havale ledger'a askı olarak yazılıyor
/// ve mesaj ack'leniyor. Dead-letter yalnızca ledger'a hiç yazılamayan havale için: o bir
/// alarm, çünkü para bankamızda ama ledger'da yok.
///
/// Kimlik sorusu cevapsız kaldıysa (onboarding kapalı) karar verilmiyor, mesaj kuyruğa
/// geri konuyor. Tek aktif tüketici olduğu için arkasındaki havaleler de bekliyor; bu
/// bilinçli: onboarding kapalıyken verilecek her karar tahmin olurdu.
/// </summary>
internal sealed class DepositConsumer(
    RabbitMqConnection connection,
    DepositTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<DepositConsumer> logger) : BackgroundService
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
                    exception, "Havale tüketicisi başlatılamadı, 5 sn sonra yeniden denenecek.");

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

        // Declare idempotent; bank-adapter önce kurmuşsa bu çağrı doğrulama yapıyor.
        await topology.DeclareAsync(channel, ct);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, ct);

        await channel.BasicConsumeAsync(topology.WalletQueue, autoAck: false, consumer, ct);

        _channel = channel;

        logger.LogInformation("Havale tüketicisi {Queue} dinliyor.", topology.WalletQueue);
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        try
        {
            var message = JsonSerializer.Deserialize<BankDepositReceived>(delivery.Body.Span, JsonOptions)
                          ?? throw new JsonException("Havale gövdesi boş.");

            using var scope = scopeFactory.CreateScope();

            await scope.ServiceProvider.GetRequiredService<ProcessDepositHandler>().HandleAsync(message, ct);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception exception) when (exception is JsonException or DepositRejectedException)
        {
            // KALICI hata ve ALARM: para bankamızda, ledger'da yok. Requeue etmek tek
            // aktif tüketicili kuyruğu süresiz tıkardı; dead-letter'daki mesaj düzeltme
            // yapılıp elle geri konuyor.
            logger.LogCritical(
                exception, "Havale ledger'a yazılamıyor, dead-letter'a gidiyor. Banka bakiyesiyle nostro ayrıştı.");

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // GEÇİCİ varsayılan her şey: DB kapalı, onboarding cevap vermiyor, çakışma.
            logger.LogWarning(exception, "Havale işlenemedi, kuyruğa geri konuyor.");

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
