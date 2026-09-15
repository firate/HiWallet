using System.Text.Json;
using HiWallet.BankAdapter.Application;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.BankAdapter.Infrastructure.Messaging;

/// <summary>
/// Orchestrator'ın transfer komutlarını dinler ve bankaya iletir.
///
/// <b>CEVAP YAYINLAMIYOR.</b> Eski sahte servis burada cevabı da yayınlıyordu;
/// artık yayın <see cref="ReplyRelay"/>'in işi ve ancak sonuç öğrenildiğinde
/// oluyor (decisions.md madde 35). Bu tüketicinin verdiği tek söz "komutu bankaya
/// ilettim".
///
/// <b>Geçici hata ile kalıcı hata AYRI.</b> Geçici hatada hiçbir şey yayınlanmıyor,
/// mesaj kuyruğa geri konuyor ve saga <c>BankTransferPending</c>'de bekliyor.
/// Kalıcı hatada mesaj dead-letter'a gidiyor — çünkü kalıcı hata burada bankanın
/// reddi değil, İSTEĞİMİZİN bozuk olması: yeniden denemek aynı sonucu verir ve
/// insan bakmalı. Bankanın kendi reddi bu yoldan hiç geçmiyor; o, kabul edilmiş
/// bir transferin sonucu olarak sonra geliyor.
/// </summary>
internal sealed class BankCommandConsumer(
    RabbitMqConnection connection,
    WithdrawalTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<BankCommandConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

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
                    exception, "Banka komut tüketicisi başlatılamadı, 5 sn sonra yeniden denenecek.");

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

        // Declare idempotent; hangi servis önce kalkarsa o kuruyor.
        await topology.DeclareAsync(channel, ct);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, ct);

        await channel.BasicConsumeAsync(topology.BankQueue, autoAck: false, consumer, ct);

        _channel = channel;

        logger.LogInformation("Banka komut tüketicisi {Queue} dinliyor.", topology.BankQueue);
    }

    private async Task OnMessageAsync(
        IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var commandName = delivery.RoutingKey;

        try
        {
            if (commandName != nameof(StartBankTransfer))
            {
                // Bu kuyruğa başka bir tip düşmemeli; düştüyse topoloji ya da
                // sözleşme bozuk. Sessizce ack'lemek onu görünmez kılardı.
                throw new UnknownCommandException(commandName);
            }

            var command = JsonSerializer.Deserialize<StartBankTransfer>(delivery.Body.Span, JsonOptions)
                          ?? throw new JsonException("StartBankTransfer gövdesi boş.");

            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<StartBankTransferHandler>();

            await handler.HandleAsync(command, ct);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (TransientBankException exception)
        {
            // Banka cevap vermedi. Saga'ya HİÇBİR ŞEY bildirilmiyor: telafi
            // başlatmak müşterinin parasını gereksiz yere ileri geri taşırdı
            // (overview.md madde 6).
            logger.LogWarning(
                "Banka cevap vermedi, komut kuyruğa geri konuyor: {Message}", exception.Message);

            await Task.Delay(RetryDelay, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
        catch (Exception exception)
            when (exception is JsonException or UnknownCommandException or PermanentBankException)
        {
            // Zehirli mesaj: aynı hata her denemede tekrarlanır. Requeue etmek
            // partition'ı süresiz tıkardı.
            logger.LogError(
                exception, "Banka komutu işlenemedi, dead-letter'a gidiyor. {Command}", commandName);

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // DB kapalı, timeout. Banka çağrısı yapılmış olabilir — sorun değil,
            // tekrar teslimde aynı Idempotency-Key ile aynı transfer dönüyor.
            logger.LogWarning(
                exception, "Banka komutu işlenemedi, kuyruğa geri konuyor. {Command}", commandName);

            await Task.Delay(RetryDelay, ct);
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

internal sealed class UnknownCommandException(string commandName)
    : Exception($"Banka kuyruğuna beklenmeyen komut düştü: {commandName}");
