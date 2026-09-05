using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Top-up hattının RabbitMQ topolojisi. Hem publish eden hem tüketen taraf bunu
/// çağırır; declare işlemleri idempotent, hangisi önce ayağa kalkarsa o kurar.
///
/// Şekil:
/// <code>
///   hiwallet.topups  (x-consistent-hash)
///        ├── hiwallet.topups.p0  ┐  x-single-active-consumer
///        ├── hiwallet.topups.p1  │  routing key = cüzdan id, hash ile partition
///        └── ...                 ┘  seçilir; aynı cüzdan hep aynı kuyrukta
///
///   hiwallet.topups.dlx (fanout) ── hiwallet.topups.dead
/// </code>
/// </summary>
public sealed class TopupTopology(IOptions<RabbitMqOptions> options)
{
    /// <summary>
    /// <c>x-consistent-hash</c> exchange'i <c>rabbitmq_consistent_hash_exchange</c>
    /// eklentisiyle geliyor; imajda etkinleştirilmiş olmalı (docker/rabbitmq/).
    /// </summary>
    private const string ConsistentHashExchangeType = "x-consistent-hash";

    private readonly RabbitMqOptions _options = options.Value;

    public string Exchange => $"{_options.NamePrefix}hiwallet.topups";

    public string DeadLetterExchange => $"{_options.NamePrefix}hiwallet.topups.dlx";

    public string DeadLetterQueue => $"{_options.NamePrefix}hiwallet.topups.dead";

    public int PartitionCount => _options.PartitionCount;

    public string PartitionQueue(int index) => $"{Exchange}.p{index}";

    public async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
            cancellationToken: ct);

        // Zehirli mesaj burada birikiyor ve elle incelenmeyi bekliyor. Otomatik
        // yeniden denenmiyor: aynı mesaj aynı hatayı vermeye devam ederse partition
        // kuyruğunu bir daha tıkar.
        await channel.QueueDeclareAsync(
            DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            DeadLetterQueue, DeadLetterExchange, routingKey: string.Empty,
            cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            Exchange, ConsistentHashExchangeType, durable: true, autoDelete: false,
            cancellationToken: ct);

        var queueArguments = new Dictionary<string, object?>
        {
            // Kuyruğu aynı anda tek bir consumer okur; diğer instance'lar sırada
            // bekler ve o düşerse devralır. Partition'ları instance'lara elle
            // dağıtmadan hem sıralama hem devir böyle çözülüyor.
            ["x-single-active-consumer"] = true,
            ["x-dead-letter-exchange"] = DeadLetterExchange
        };

        for (var partition = 0; partition < PartitionCount; partition++)
        {
            var queue = PartitionQueue(partition);

            await channel.QueueDeclareAsync(
                queue, durable: true, exclusive: false, autoDelete: false,
                arguments: queueArguments, cancellationToken: ct);

            // Consistent hash exchange'te binding'in routing key'i AĞIRLIKTIR,
            // eşleşme deseni değil. Hepsi "1": hash halkasında eşit pay.
            await channel.QueueBindAsync(
                queue, Exchange, routingKey: "1", cancellationToken: ct);
        }
    }
}
