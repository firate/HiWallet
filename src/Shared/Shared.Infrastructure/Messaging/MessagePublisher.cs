using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Tek mesaj yayınlar, publisher confirms açık. Üç servis de kullanıyor
/// (orchestrator komut, wallet ve bank event), o yüzden Shared'da.
///
/// <b>Onay neden açık.</b> Onaysız publish "gönderdim" der ama broker'ın yazdığını
/// garanti etmez; kayıp sessiz olur. Açıkken <c>BasicPublishAsync</c> onayı
/// bekliyor ve reddedilirse istisna atıyor — çağıran hatayı görüyor ve mesajı
/// outbox'ta bırakabiliyor.
///
/// <b>Transaction YOK.</b> Broker ile veritabanı arasında ortak transaction olamaz;
/// güvenilirlik outbox + en az bir kez teslim + tüketici tarafında deduplikasyon
/// ile sağlanıyor (overview.md madde 5 ve 6).
/// </summary>
public sealed class MessagePublisher(
    RabbitMqConnection connection, ILogger<MessagePublisher> logger) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    /// <param name="messageId">
    /// Tüketici tarafındaki deduplikasyonun anahtarı. Komutlarda <c>CommandId</c>,
    /// event'lerde saga id + tip. Broker'ın kendi tekilliği yok — bu alan
    /// <c>processed_messages</c>'a yazılan değer.
    /// </param>
    public Task PublishAsync<T>(
        string exchange, string routingKey, T message, Guid messageId, CancellationToken ct) =>
        PublishRawAsync(
            exchange,
            routingKey,
            JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions),
            messageId,
            typeof(T).Name,
            ct);

    /// <summary>
    /// Hazır gövdeyi yayınlar. Çağıranın elinde zaten serileştirilmiş bir JSON varsa
    /// (outbox satırı, saklanmış bir cevap) onu tipe geri çevirip yeniden
    /// serileştirmenin anlamı yok — üstelik iki dönüşüm arasındaki her fark tel
    /// üzerinde farklı bir mesaj demek.
    /// </summary>
    /// <param name="type">
    /// AMQP <c>type</c> başlığı ve genelde routing key ile aynı: mesaj tipinin adı.
    /// </param>
    public async Task PublishRawAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        Guid messageId,
        string type,
        CancellationToken ct)
    {
        var channel = await GetChannelAsync(ct);

        var properties = new BasicProperties
        {
            // Broker yeniden başlarsa mesaj hayatta kalsın.
            Persistent = true,
            ContentType = "application/json",
            MessageId = messageId.ToString(),
            Type = type
        };

        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: routingKey,
            // Yönlendirilemeyen mesaj sessizce düşer; mandatory onu görünür kılıyor.
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);
    }

    private async ValueTask<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        await _gate.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true }) return _channel;

            if (_channel is not null) await _channel.DisposeAsync();

            var current = await connection.GetAsync(ct);

            _channel = await current.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                ct);

            // mandatory tek başına yetmiyor: yönlendirilemeyen mesaj istisna
            // fırlatmıyor, geri dönüyor. Bu olay olmadan sessizce kaybolurdu.
            // Beklenen bir durum DEĞİL — çıkarsa topoloji bozuk demektir.
            _channel.BasicReturnAsync += (_, returned) =>
            {
                logger.LogError(
                    "Mesaj hiçbir kuyruğa yönlendirilemedi: {ReplyText}. Exchange {Exchange}, routing key {RoutingKey}",
                    returned.ReplyText, returned.Exchange, returned.RoutingKey);

                return Task.CompletedTask;
            };

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        _gate.Dispose();
    }
}
