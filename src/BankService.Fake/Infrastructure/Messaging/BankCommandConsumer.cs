using System.Text;
using System.Text.Json;
using HiWallet.BankService.Application;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.BankService.Infrastructure.Messaging;

/// <summary>
/// Orchestrator'ın gönderdiği transfer komutlarını dinler, handler'a verir ve cevabı
/// yayınlar.
///
/// <b>Sıra: transferi kaydet → cevabı yayınla → ack.</b> Wallet tarafındaki kalıbın
/// aynısı. Yayın başarısız olursa ack yok, komut yeniden teslim ediliyor ve o
/// teslimde transfer TEKRARLANMADAN saklanan cevap yeniden gönderiliyor.
///
/// <b>Geçici hata ile kalıcı hata farklı ele alınıyor.</b> Kalıcı hata bir CEVAP:
/// <c>BankTransferFailed</c> yayınlanıp mesaj ack'leniyor, saga telafiye geçiyor.
/// Geçici hata cevap değil: hiçbir şey yayınlanmıyor, mesaj kuyruğa geri konuyor ve
/// saga <c>BankTransferPending</c>'de beklemeye devam ediyor. İkisi karıştırılsaydı
/// her ağ kesintisi müşterinin parasını ileri geri taşırdı (overview.md madde 6).
/// </summary>
internal sealed class BankCommandConsumer(
    RabbitMqConnection connection,
    WithdrawalTopology topology,
    MessagePublisher publisher,
    IServiceScopeFactory scopeFactory,
    ILogger<BankCommandConsumer> logger) : BackgroundService
{
    /// <summary>
    /// Geçici hatadan sonra yeniden denemeden önce beklenen süre. Gerçek bir banka
    /// entegrasyonunda burada üstel geri çekilme olurdu; sahte serviste sabit süre
    /// testleri öngörülebilir tutuyor.
    /// </summary>
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

            var reply = await handler.HandleAsync(command, ct);

            await publisher.PublishRawAsync(
                topology.Exchange,
                reply.RoutingKey,
                Encoding.UTF8.GetBytes(reply.Payload),
                messageId: command.CommandId,
                type: reply.RoutingKey,
                ct);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (TransientBankFailureException exception)
        {
            // Beklenen bir durum, hata değil: senaryo bunu istedi. Saga'ya HİÇBİR
            // ŞEY bildirilmiyor; mesaj geri konuyor ve bir sonraki denemede senaryo
            // ilerliyor.
            logger.LogInformation(
                "{Message} Mesaj kuyruğa geri konuyor.", exception.Message);

            await Task.Delay(RetryDelay, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
        catch (Exception exception) when (exception is JsonException or UnknownCommandException)
        {
            logger.LogError(
                exception, "Banka komutu çözümlenemedi, dead-letter'a gidiyor. {Command}", commandName);

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // DB kapalı, broker yayını reddetti, timeout. Yayın burada patlarsa
            // transfer KAYDEDİLMİŞ olabilir — sorun değil, tekrar teslimde saklanan
            // cevap yeniden yayınlanıyor.
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
