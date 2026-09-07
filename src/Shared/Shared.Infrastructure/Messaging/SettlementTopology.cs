using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Settlement bildirimlerinin topolojisi. topup-webhook yayınlıyor, wallet-consumer
/// tüketiyor.
///
/// <b>Top-up'takinden farklı: partition YOK.</b> Orada sıra cüzdan başına önemliydi
/// ve routing key cüzdan kimliğiydi. Settlement hiçbir cüzdana dokunmuyor — yalnızca
/// sistem hesaplarını hareket ettiriyor (<c>clearing</c>, <c>provider_expense</c>,
/// <c>nostro</c>). Cüzdan bazlı partition'ın koruduğu şey burada yok, o yüzden
/// consistent hash de yok; eklemek <c>x-consistent-hash</c> eklentisine gereksiz bir
/// bağımlılık olurdu.
///
/// <b>Sıra sağlayıcı bazında bile gerekmiyor.</b> Her settlement kendi batch'ini
/// kapatıyor ve kapattığı alacak başka bir batch'in konusu değil; iki settlement'ın
/// ters sırada işlenmesi aynı clearing bakiyesini veriyor.
///
/// Şekil — tek direct exchange, tek kuyruk:
/// <code>
///   hiwallet.settlements  (direct)
///        ├── .wallet        ← SettlementReceived
///        └── .invoices      ← ProviderInvoiceReceived
///
///   hiwallet.settlements.dlx (fanout) ── .dead
/// </code>
/// </summary>
public sealed class SettlementTopology(IOptions<RabbitMqOptions> options)
{
    /// <summary>
    /// Tek routing key. Sözleşme tipinin adıyla aynı tutuluyor — withdrawal
    /// topolojisindeki gerekçe: tip yeniden adlandırıldığında binding de derleme
    /// zamanında değişsin.
    /// </summary>
    public const string RoutingKey = nameof(Contracts.Settlements.SettlementReceived);

    /// <summary>Dönem sonu faturası (adım 5.6). Aynı exchange, ayrı kuyruk.</summary>
    public const string InvoiceRoutingKey = nameof(Contracts.Settlements.ProviderInvoiceReceived);

    private readonly RabbitMqOptions _options = options.Value;

    public string Exchange => $"{_options.NamePrefix}hiwallet.settlements";

    public string DeadLetterExchange => $"{Exchange}.dlx";

    public string DeadLetterQueue => $"{Exchange}.dead";

    public string WalletQueue => $"{Exchange}.wallet";

    /// <summary>
    /// Fatura kuyruğu AYRI. Aynı kuyruğa bağlanabilirdi ama takılmış bir fatura
    /// (incelemeye düşmüş, tekrar tekrar teslim edilen) settlement akışını da
    /// durdururdu — ikisi bağımsız ve biri para girişini kapatıyor.
    /// </summary>
    public string InvoiceQueue => $"{Exchange}.invoices";

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
                // Zehirli mesaj kuyruğu tıkamasın; otomatik yeniden denenmiyor.
                ["x-dead-letter-exchange"] = DeadLetterExchange
            },
            cancellationToken: ct);

        await channel.QueueBindAsync(WalletQueue, Exchange, RoutingKey, cancellationToken: ct);

        await channel.QueueDeclareAsync(
            InvoiceQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange
            },
            cancellationToken: ct);

        await channel.QueueBindAsync(
            InvoiceQueue, Exchange, InvoiceRoutingKey, cancellationToken: ct);
    }
}
