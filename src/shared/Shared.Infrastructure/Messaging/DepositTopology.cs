using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Gelen havalelerin topolojisi. <c>bank-adapter</c> yayınlıyor, <c>wallet-consumer</c>
/// tüketiyor.
///
/// <b>Tek kuyruk ve tek aktif tüketici.</b> Mesaj yayınlanırken cüzdan bilinmiyor, onu
/// açıklamadaki numaradan wallet buluyor. Sıra bir şeyi koruyor: aynı hesaba gelen iki
/// havale aynı anda işlenirse ikisi de seviyenin aylık limitini ayrı ayrı yeterli
/// görebilirdi.
/// <c>x-single-active-consumer</c> ve <c>prefetch=1</c> havaleleri birer birer işletiyor;
/// hacim düşük, bedeli yok.
///
/// Şekil — tek direct exchange, tek kuyruk:
/// <code>
///   hiwallet.deposits  (direct)
///        └── .wallet        ← BankDepositReceived
///
///   hiwallet.deposits.dlx (fanout) ── .dead
/// </code>
/// </summary>
public sealed class DepositTopology(IOptions<RabbitMqOptions> options)
{
    /// <summary>Sözleşme tipinin adıyla aynı (settlement topolojisindeki gerekçe).</summary>
    public const string RoutingKey = nameof(Contracts.Deposits.BankDepositReceived);

    private readonly RabbitMqOptions _options = options.Value;

    public string Exchange => $"{_options.NamePrefix}hiwallet.deposits";

    public string DeadLetterExchange => $"{Exchange}.dlx";

    /// <summary>
    /// Dead-letter'a düşen havale bir ALARM: para bankamızda ama ledger'da yok.
    /// Kendiliğinden yeniden denenmiyor; düzeltme yapılıp elle geri konuyor.
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
