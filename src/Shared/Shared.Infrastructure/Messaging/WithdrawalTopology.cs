using HiWallet.Shared.Contracts.Withdrawals;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Withdrawal saga'sının topolojisi. Üç servis de bunu çağırıyor; declare
/// idempotent, hangisi önce kalkarsa o kuruyor.
///
/// <b>Top-up'takinden farklı: partition YOK.</b> Orada aynı cüzdanın mesajlarının
/// sırası önemliydi ve mesajlar tek bir tüketici havuzuna akıyordu. Burada her
/// mesajın belli bir alıcısı var ve sıra saga'nın kendisinden geliyor — refund
/// komutu ancak debit tamamlandıktan sonra gönderiliyor, yani nedensel olarak
/// zaten sıralı. Consistent hash eklemek karşılığı olmayan bir karmaşıklık olurdu.
///
/// Şekil — tek direct exchange, alıcı başına bir kuyruk, routing key = mesaj tipi:
/// <code>
///   hiwallet.withdrawals  (direct)
///        ├── .wallet        ← DebitForWithdrawal, RefundWithdrawal
///        ├── .bank          ← StartBankTransfer
///        └── .orchestrator  ← WithdrawalDebited, WithdrawalDebitRejected,
///                             BankTransferSucceeded, BankTransferFailed,
///                             WithdrawalRefunded
///
///   hiwallet.withdrawals.dlx (fanout) ── .dead
/// </code>
/// </summary>
public sealed class WithdrawalTopology(IOptions<RabbitMqOptions> options)
{
    private readonly RabbitMqOptions _options = options.Value;

    public string Exchange => $"{_options.NamePrefix}hiwallet.withdrawals";

    public string DeadLetterExchange => $"{Exchange}.dlx";

    public string DeadLetterQueue => $"{Exchange}.dead";

    /// <summary>wallet-service'in komut kuyruğu.</summary>
    public string WalletQueue => $"{Exchange}.wallet";

    /// <summary>bank-service'in komut kuyruğu.</summary>
    public string BankQueue => $"{Exchange}.bank";

    /// <summary>orchestrator'ın event kuyruğu.</summary>
    public string OrchestratorQueue => $"{Exchange}.orchestrator";

    /// <summary>
    /// Routing key = mesaj tipinin adı. Tip adını kullanmak, sözleşme sınıfı
    /// yeniden adlandırıldığında binding'in de derleme zamanında değişmesini
    /// sağlıyor; elle yazılmış string olsaydı sessizce ayrışırdı.
    /// </summary>
    public static string RoutingKeyFor<T>() => typeof(T).Name;

    private static readonly string[] WalletKeys =
    [
        nameof(DebitForWithdrawal),
        nameof(RefundWithdrawal)
    ];

    private static readonly string[] BankKeys =
    [
        nameof(StartBankTransfer)
    ];

    private static readonly string[] OrchestratorKeys =
    [
        nameof(WithdrawalDebited),
        nameof(WithdrawalDebitRejected),
        nameof(BankTransferSucceeded),
        nameof(BankTransferFailed),
        nameof(WithdrawalRefunded)
    ];

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

        await DeclareQueueAsync(channel, WalletQueue, WalletKeys, ct);
        await DeclareQueueAsync(channel, BankQueue, BankKeys, ct);
        await DeclareQueueAsync(channel, OrchestratorQueue, OrchestratorKeys, ct);
    }

    private async Task DeclareQueueAsync(
        IChannel channel, string queue, string[] routingKeys, CancellationToken ct)
    {
        var arguments = new Dictionary<string, object?>
        {
            // Zehirli mesaj kuyruğu tıkamasın. Otomatik yeniden denenmiyor: aynı
            // mesaj aynı hatayı vermeye devam eder.
            ["x-dead-letter-exchange"] = DeadLetterExchange
        };

        await channel.QueueDeclareAsync(
            queue, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments, cancellationToken: ct);

        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(queue, Exchange, routingKey, cancellationToken: ct);
        }
    }
}
