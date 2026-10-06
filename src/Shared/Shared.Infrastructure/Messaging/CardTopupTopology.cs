using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Kartla yüklemenin kapanışları. <c>card-topup</c> yayınlıyor, <c>wallet-consumer</c>
/// tüketiyor.
///
/// <b>Partition YOK, tek kuyruk ve tek aktif tüketici</b>, havaledeki gibi
/// (<see cref="DepositTopology"/>): ödenen yüklemeler aynı clearing satırını güncelliyor ve
/// hacim düşük. <c>x-single-active-consumer</c> ve <c>prefetch=1</c> kapanışları birer birer
/// işletiyor.
///
/// Şekil — tek direct exchange, tek kuyruk:
/// <code>
///   hiwallet.card-topups  (direct)
///        └── .wallet        ← CardTopupClosed
///
///   hiwallet.card-topups.dlx (fanout) ── .dead
/// </code>
/// </summary>
public sealed class CardTopupTopology(IOptions<RabbitMqOptions> options)
{
    /// <summary>Sözleşme tipinin adıyla aynı (settlement topolojisindeki gerekçe).</summary>
    public const string RoutingKey = nameof(Contracts.CardTopups.CardTopupClosed);

    private readonly RabbitMqOptions _options = options.Value;

    public string Exchange => $"{_options.NamePrefix}hiwallet.card-topups";

    public string DeadLetterExchange => $"{Exchange}.dlx";

    /// <summary>
    /// Dead-letter'a düşen kapanış bir ALARM: kart yüklemesi servisi ile wallet ayrışmış.
    /// Kendiliğinden yeniden denenmiyor; sebep incelenip elle geri konuyor.
    /// </summary>
    public string DeadLetterQueue => $"{Exchange}.dead";

    public string WalletQueue => $"{Exchange}.wallet";

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
            WalletQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-single-active-consumer"] = true
            },
            cancellationToken: ct);

        await channel.QueueBindAsync(WalletQueue, Exchange, RoutingKey, cancellationToken: ct);
    }
}
