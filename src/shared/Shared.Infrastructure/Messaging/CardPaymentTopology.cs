using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Kart sağlayıcısının ödeme bildirimleri. <c>topup-webhook</c> yayınlıyor (inbox
/// relay'i), <c>card-topup</c> tüketiyor.
///
/// <b>Partition YOK, tek kuyruk ve tek aktif tüketici.</b> Mesaj yayınlanırken cüzdan
/// bilinmiyor; ödemeyi kendi kaydıyla kart yüklemesi servisi eşleştiriyor. Aynı ödemenin
/// iki bildirimi (önce vazgeçildi, sonra ödendi gibi bir çelişki) sırayla işlensin; hacim
/// düşük.
///
/// Şekil — tek direct exchange, tek kuyruk:
/// <code>
///   hiwallet.card-payments  (direct)
///        └── .card-topup    ← CardPaymentUpdated
///
///   hiwallet.card-payments.dlx (fanout) ── .dead
/// </code>
/// </summary>
public sealed class CardPaymentTopology(IOptions<RabbitMqOptions> options)
{
    /// <summary>Sözleşme tipinin adıyla aynı (settlement topolojisindeki gerekçe).</summary>
    public const string RoutingKey = nameof(Contracts.CardPayments.CardPaymentUpdated);

    private readonly RabbitMqOptions _options = options.Value;

    public string Exchange => $"{_options.NamePrefix}hiwallet.card-payments";

    public string DeadLetterExchange => $"{Exchange}.dlx";

    /// <summary>
    /// Dead-letter'a düşen bildirim bir ALARM: sağlayıcı bizim açmadığımız ya da kaydımızla
    /// uyuşmayan bir ödemeyi bildiriyor. Elle inceleniyor.
    /// </summary>
    public string DeadLetterQueue => $"{Exchange}.dead";

    public string CardTopupQueue => $"{Exchange}.card-topup";

    public async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            DeadLetterQueue, DeadLetterExchange, routingKey: string.Empty, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            Exchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            CardTopupQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-single-active-consumer"] = true
            },
            cancellationToken: ct);

        await channel.QueueBindAsync(CardTopupQueue, Exchange, RoutingKey, cancellationToken: ct);
    }
}
