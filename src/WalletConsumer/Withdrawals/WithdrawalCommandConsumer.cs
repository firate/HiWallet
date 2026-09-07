using System.Text;
using System.Text.Json;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletService.Application.Withdrawals;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HiWallet.WalletConsumer.Withdrawals;

/// <summary>
/// Orchestrator'ın wallet'a gönderdiği komutları dinler, handler'a verir ve cevabı
/// yayınlar.
///
/// <b>Sıra: ledger commit → cevabı yayınla → ack.</b> Yayın başarısız olursa mesaj
/// ack'lenmiyor ve komut yeniden teslim ediliyor; o teslimde handler ledger'a
/// dokunmadan SAKLANAN cevabı dönüyor ve yayın tekrar deneniyor
/// (<c>processed_messages</c>, decisions.md madde 32). Bu yüzden burada outbox yok:
/// yeniden deneme broker'ın redelivery'sinden geliyor.
///
/// Ters sıra (önce ack, sonra yayınla) cevabı KAYBEDERDİ ve saga sonsuza kadar
/// beklerdi — para cüzdandan çıkmış, kimse haber alamamış olurdu.
///
/// <b>Bu sınıf yalnızca TAŞIMA.</b> Ledger'a yazan iş kuralı
/// <c>WalletService.Core</c>'da ve wallet-api ile paylaşılıyor (madde 25).
/// </summary>
internal sealed class WithdrawalCommandConsumer(
    RabbitMqConnection connection,
    WithdrawalTopology topology,
    MessagePublisher publisher,
    IServiceScopeFactory scopeFactory,
    ILogger<WithdrawalCommandConsumer> logger) : BackgroundService
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
                    exception, "Çekim komut tüketicisi başlatılamadı, 5 sn sonra yeniden denenecek.");

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

        // Declare idempotent; orchestrator önce kurmuşsa bu çağrı doğrulama yapıyor.
        await topology.DeclareAsync(channel, ct);

        // prefetch=1: aynı cüzdana iki komut aynı anda işlenmesin. Daha yükseğe
        // çıkarılabilir — ledger_balances.version ikinci yazarı zaten reddediyor —
        // ama çekim hacmi bunu gerektirmiyor.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, ct);

        await channel.BasicConsumeAsync(topology.WalletQueue, autoAck: false, consumer, ct);

        _channel = channel;

        logger.LogInformation("Çekim komut tüketicisi {Queue} dinliyor.", topology.WalletQueue);
    }

    private async Task OnMessageAsync(
        IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        // Routing key = komut tipinin adı (WithdrawalTopology).
        var commandName = delivery.RoutingKey;

        try
        {
            using var scope = scopeFactory.CreateScope();

            var reply = await DispatchAsync(scope.ServiceProvider, commandName, delivery.Body.Span, ct);

            // MessageId alıcı tarafın (orchestrator) korelasyonu değil, kendi
            // deduplikasyonu için: event'lerde saga durumu zaten yeterli ama başlık
            // teşhiste "hangi komuta cevap" sorusunu ucuza cevaplıyor.
            await publisher.PublishRawAsync(
                topology.Exchange,
                reply.RoutingKey,
                Encoding.UTF8.GetBytes(reply.Payload),
                messageId: MessageId(delivery),
                type: reply.RoutingKey,
                ct);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);

            if (reply.Replayed)
            {
                logger.LogInformation(
                    "{Command} tekrar teslim edildi; ledger'a dokunulmadı, saklanan cevap yeniden yayınlandı.",
                    commandName);
            }
        }
        catch (Exception exception) when (exception is JsonException or UnknownCommandException)
        {
            // Gövde bozuk ya da sözleşme tanınmıyor. Aynı bayt aynı sonucu verir.
            logger.LogError(
                exception, "Çekim komutu çözümlenemedi, dead-letter'a gidiyor. {Command}", commandName);

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Kapanıyoruz. Mesaj ack'lenmedi, sonraki açılışta yeniden teslim edilecek.
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Aynı bakiyeye başka bir yazar yetişti; yeni anlık görüntüyle yeniden
            // denenmeli.
            logger.LogWarning("Çekim komutu çakıştı, kuyruğa geri konuyor. {Command}", commandName);

            await Task.Delay(RequeueDelay, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
        catch (Exception exception)
        {
            // Geçici varsayılan her şey: DB kapalı, broker yayını reddetti, timeout.
            // Yayın burada patlarsa ledger COMMIT EDİLMİŞ olabilir — sorun değil,
            // tekrar teslimde saklanan cevap yeniden yayınlanıyor.
            logger.LogWarning(
                exception, "Çekim komutu işlenemedi, kuyruğa geri konuyor. {Command}", commandName);

            await Task.Delay(RequeueDelay, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
        }
    }

    private static Task<WithdrawalReply> DispatchAsync(
        IServiceProvider services, string commandName, ReadOnlySpan<byte> body, CancellationToken ct)
    {
        return commandName switch
        {
            nameof(DebitForWithdrawal) => services
                .GetRequiredService<DebitForWithdrawalHandler>()
                .HandleAsync(Read<DebitForWithdrawal>(body), ct),

            nameof(RefundWithdrawal) => services
                .GetRequiredService<RefundWithdrawalHandler>()
                .HandleAsync(Read<RefundWithdrawal>(body), ct),

            nameof(SettleWithdrawal) => services
                .GetRequiredService<SettleWithdrawalHandler>()
                .HandleAsync(Read<SettleWithdrawal>(body), ct),

            _ => throw new UnknownCommandException(commandName)
        };
    }

    private static T Read<T>(ReadOnlySpan<byte> body) =>
        JsonSerializer.Deserialize<T>(body, JsonOptions)
        ?? throw new JsonException($"{typeof(T).Name} gövdesi boş.");

    /// <summary>
    /// Cevabın kimliği komutun kimliğinden türetiliyor: aynı komut yeniden teslim
    /// edilirse aynı cevap kimliği gidiyor. Yeni bir guid üretilseydi orchestrator
    /// tarafında iki farklı mesaj gibi görünürdü.
    /// </summary>
    private static Guid MessageId(BasicDeliverEventArgs delivery) =>
        Guid.TryParse(delivery.BasicProperties.MessageId, out var id) ? id : Guid.NewGuid();

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_channel is not null)
        {
            // Yalnızca DisposeAsync: kanalı zaten kapatıyor. CloseAsync ile birlikte
            // çağrılması otomatik kurtarmayı disposed kanala sokuyor.
            await _channel.DisposeAsync();
            _channel = null;
        }
    }
}

internal sealed class UnknownCommandException(string commandName)
    : Exception($"Tanınmayan çekim komutu: {commandName}");
