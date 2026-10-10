using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.Shared.Contracts.Withdrawals;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Orchestrator'ın saga'larının topolojisi: çekim ve askıdaki havalenin iadesi. Üç servis
/// de bunu çağırıyor; declare idempotent, hangisi önce kalkarsa o kuruyor. İki saga aynı
/// kuyrukları paylaşıyor: alıcılar aynı, mesaj tipi routing key'de.
///
/// <b>Sıra saga'nın kendisinden geliyor.</b> Her mesajın belli bir alıcısı var ve refund
/// komutu ancak debit tamamlandıktan sonra gönderiliyor, yani mesajlar nedensel olarak
/// zaten sıralı.
///
/// Şekil — tek direct exchange, alıcı başına bir kuyruk, routing key = mesaj tipi:
/// <code>
///   hiwallet.withdrawals  (direct)
///        ├── .wallet        ← DebitForWithdrawal, RefundWithdrawal, SettleWithdrawal,
///        │                    DebitSuspenseForReturn, SettleDepositReturn,
///        │                    RestoreSuspendedDeposit
///        ├── .bank          ← StartBankTransfer, ReturnBankDeposit
///        └── .orchestrator  ← WithdrawalDebited, WithdrawalDebitRejected,
///                             BankTransferSucceeded, BankTransferFailed,
///                             WithdrawalRefunded, WithdrawalSettled,
///                             SuspenseDebitedForReturn, SuspenseDebitForReturnRejected,
///                             DepositReturnSettled, SuspendedDepositRestored
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
        nameof(RefundWithdrawal),
        nameof(SettleWithdrawal),
        nameof(DebitSuspenseForReturn),
        nameof(SettleDepositReturn),
        nameof(RestoreSuspendedDeposit)
    ];

    private static readonly string[] BankKeys =
    [
        nameof(StartBankTransfer),
        nameof(ReturnBankDeposit)
    ];

    private static readonly string[] OrchestratorKeys =
    [
        nameof(WithdrawalDebited),
        nameof(WithdrawalDebitRejected),
        nameof(BankTransferSucceeded),
        nameof(BankTransferFailed),
        nameof(WithdrawalRefunded),
        nameof(WithdrawalSettled),
        nameof(SuspenseDebitedForReturn),
        nameof(SuspenseDebitForReturnRejected),
        nameof(DepositReturnSettled),
        nameof(SuspendedDepositRestored)
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
