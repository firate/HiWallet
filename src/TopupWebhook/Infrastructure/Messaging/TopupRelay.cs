using System.Text;
using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.TopupWebhook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
/// <b>TEK INSTANCE.</b> Tur <c>pg_try_advisory_lock</c> ile korunuyor
/// (decisions.md madde 30); kilidi alamayan instance o turu atlıyor. Gerekçe
/// SIRALAMA: iki relay ayrı batch'ler alıp farklı hızda yayınlarsa aynı cüzdanın
/// iki event'i exchange'e TERS SIRADA varıyor ve kuyruğun içindeki sıra garantisi
/// bunu düzeltmiyor. Bedeli açık — relay yatay ölçeklenmiyor.
///
/// <b><c>FOR UPDATE SKIP LOCKED</c> KALIYOR.</b> Kilitle gereksizleşmiş gibi duruyor
/// ama ikinci emniyet kemeri: kilit "aynı anda tek relay" diyor, SKIP LOCKED ise
/// kilit bir şekilde alınamadığında iki relay'in aynı SATIRI almasını engelliyor.
/// Birincisi sıra için, ikincisi çift yayın için.
/// </summary>
internal sealed class TopupRelay(
    IDbContextFactory<InboxDbContext> contextFactory,
    JobLease lease,
    RabbitMqConnection connection,
    TopupTopology topupTopology,
    SettlementTopology settlementTopology,
    TimeProvider timeProvider,
    ILogger<TopupRelay> logger) : BackgroundService
{
    /// <summary>Kilit anahtarının kaynağı; bütün instance'larda AYNI olmak zorunda.</summary>
    private const string JobName = "topup:relay";

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
                var published = 0;

                // Kilit TUR BAŞINA alınıyor, ömür boyu tutulmuyor. Ömür boyu tutmak
                // "kilidi tutan süreç öldü ama bağlantı kapanmadı" durumunda relay'i
                // tamamen durdururdu; tur başına almak en kötü ihtimalle bir turluk
                // gecikme üretiyor.
                //
                // Turlar birbiriyle örtüşmediği için sıra korunuyor: bir sonraki
                // batch'i kim alırsa alsın, önceki batch çoktan yayınlanmış oluyor.
                var ran = await lease.TryRunAsync(
                    JobName,
                    async ct => published = await PublishBatchAsync(ct),
                    stoppingToken);

                // Kilit başkasındaysa bu instance boşta bekliyor. Dolu batch
                // geldiyse hemen devam: birikmiş kuyruğu boşaltırken yarım saniye
                // beklemenin anlamı yok.
                if (!ran || published < BatchSize)
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

    private async Task PublishAsync(IChannel channel, InboxMessage message, CancellationToken ct)
    {
        // Exchange ve mesaj tipi satırın kendi alanlarından geliyor. Relay iki akışı
        // da taşıyor ama içeriği hakkında hiçbir şey bilmiyor — payload'ı parse
        // etmiyor, yalnızca baytları ve routing key'i geçiriyor.
        var (exchange, type) = message.Kind switch
        {
            InboxKind.Topup => (topupTopology.Exchange, nameof(Shared.Contracts.Topups.TopupReceived)),
            // Settlement ve fatura AYNI exchange'de, ayrı routing key'lerle. Tip
            // routing key'in kendisi: ikisi de sözleşme tipinin adını taşıyor, o
            // yüzden sabit yazmak faturayı "SettlementReceived" diye etiketlerdi.
            InboxKind.Settlement => (settlementTopology.Exchange, message.RoutingKey),
            _ => throw new ArgumentOutOfRangeException(
                nameof(message), message.Kind, "Eşlemesi yazılmamış inbox tipi.")
        };

        var properties = new BasicProperties
        {
            // Broker yeniden başlarsa mesaj hayatta kalsın.
            Persistent = true,
            ContentType = "application/json",
            MessageId = $"{message.Provider}:{message.EventId}",
            Type = type,
            Timestamp = new AmqpTimestamp(message.ReceivedAt.ToUnixTimeSeconds())
        };

        // Top-up'ta routing key cüzdan id: consistent hash exchange bunu hash'leyip
        // partition seçiyor, aynı cüzdan hep aynı kuyruğa (overview.md madde 8).
        // Settlement'ta sabit mesaj tipi, direct exchange üzerinden tek kuyruğa.
        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: message.RoutingKey,
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

        // İki topoloji de burada kuruluyor: relay ikisine de yayınlıyor ve declare
        // idempotent. Yalnızca biri kurulsaydı, ilk settlement mesajı NOT_FOUND ile
        // düşerdi — ve bu ancak ilk settlement geldiğinde ortaya çıkardı.
        await topupTopology.DeclareAsync(_channel, ct);
        await settlementTopology.DeclareAsync(_channel, ct);

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
