using HiWallet.Shared.Contracts.Actors;

namespace HiWallet.WithdrawalOrchestrator.Domain;

/// <summary>
/// Askıdaki havalenin göndericiye iadesi. Çekim saga'sının kalıbı: wallet parayı düşüyor,
/// banka gönderiyor, sonuca göre muhasebe kapanıyor ya da para geri konuyor. Fark parayı
/// kimin beklediğinde: düşülen cüzdan değil askı, geri konan da askıya dönüyor.
///
/// Saf: DB, mesajlaşma ve zaman bilmiyor. Tutarı, para birimini ve havalenin bankasını
/// başlangıçta BİLMİYOR; çalışandan yalnızca havalenin kimliği geliyor, gerisini wallet
/// düşerken bildiriyor.
/// </summary>
public sealed class DepositReturnSaga
{
    private DepositReturnSaga()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    /// <summary>İade edilen havalenin askı kaydı. Idempotency kapsamı da bu.</summary>
    public Guid SuspendedDepositId { get; private set; }

    /// <summary>İadeyi isteyen çalışanın <c>sub</c>'ı; askıdan düşme kaydının aktörü.</summary>
    public string RequestedBy { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public DepositReturnState State { get; private set; }

    /// <summary>Havalenin tutarı; wallet düşene kadar NULL.</summary>
    public decimal? Amount { get; private set; }

    public string? Currency { get; private set; }

    /// <summary>Havaleyi alan banka; iade de ondan gidiyor.</summary>
    public string? Provider { get; private set; }

    /// <summary>Havalenin bankadaki gelen işlem referansı.</summary>
    public string? DepositBankReference { get; private set; }

    public Guid? DebitTransactionId { get; private set; }

    /// <summary>Bankaya giden komutun kimliği; bankanın idempotency anahtarı.</summary>
    public Guid? BankCommandId { get; private set; }

    /// <summary>İade transferinin bankadaki referansı.</summary>
    public string? BankReference { get; private set; }

    /// <summary>Bankanın kestiği ücret; platform yükleniyor.</summary>
    public decimal? BankFee { get; private set; }

    public Guid? SettlementTransactionId { get; private set; }

    /// <summary>Askıya geri koyan kaydın ledger işlemi.</summary>
    public Guid? RestoreTransactionId { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>
    /// Sebebin makinenin okuyacağı adı: reddetmede wallet'ın kuralı, banka reddinde
    /// <see cref="DepositReturnFailureRules.BankRejected"/>.
    /// </summary>
    public string? FailureRule { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic lock; çekim saga'sındakiyle aynı gerekçe.</summary>
    public long Version { get; private set; }

    public bool IsTerminal => State.IsTerminal();

    public static DepositReturnSaga Start(
        Guid id, Guid suspendedDepositId, string requestedBy, string idempotencyKey, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return new DepositReturnSaga
        {
            Id = id,
            SuspendedDepositId = suspendedDepositId,
            RequestedBy = requestedBy,
            IdempotencyKey = idempotencyKey,
            State = DepositReturnState.Initiated,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 0
        };
    }

    /// <summary>İsteyen çalışan; askıdan düşme komutunun aktörü.</summary>
    public CommandActor RequesterActor() => new() { Type = ActorTypes.Employee, Id = RequestedBy };

    /// <summary>Saga'nın kendi ürettiği komutların aktörü: bankanın sonucuna verilen tepkiler.</summary>
    public static CommandActor SagaActor { get; } = new() { Type = ActorTypes.System, Id = SystemFlows.DepositReturn };

    /// <summary>Wallet düşmedi. Yalnızca hiç para hareketi olmamışken geçerli.</summary>
    public TransitionResult Rejected(string reason, string rule, DateTimeOffset now)
    {
        if (State is DepositReturnState.Rejected) return TransitionResult.Ignored;

        if (State is not DepositReturnState.Initiated) return TransitionResult.Conflict;

        FailureReason = reason;
        FailureRule = rule;

        return Advance(DepositReturnState.Rejected, now);
    }

    /// <summary>
    /// Askıdan düşüldü ve banka komutu üretildi; ikisi aynı geçişte. Ayrı geçişler olsaydı
    /// aradaki çökme "düşüldü ama banka komutu yok" bırakırdı.
    /// </summary>
    public TransitionResult Debited(
        Guid ledgerTransactionId,
        decimal amount,
        string currency,
        string provider,
        string depositBankReference,
        Guid bankCommandId,
        DateTimeOffset now)
    {
        if (State is DepositReturnState.BankTransferPending
            or DepositReturnState.Settling
            or DepositReturnState.Completed
            or DepositReturnState.Restoring
            or DepositReturnState.Failed)
        {
            return TransitionResult.Ignored;
        }

        // Reddedilmiş iadede wallet düşmedi; düşüldü cevabı gelmesi çelişki.
        if (State is not DepositReturnState.Initiated) return TransitionResult.Conflict;

        DebitTransactionId = ledgerTransactionId;
        Amount = amount;
        Currency = currency;
        Provider = provider;
        DepositBankReference = depositBankReference;
        BankCommandId = bankCommandId;

        return Advance(DepositReturnState.BankTransferPending, now);
    }

    /// <param name="feeAmount">Saklanıyor: kapanış komutu yeniden gönderilebilmeli.</param>
    public TransitionResult BankTransferSucceeded(string bankReference, decimal feeAmount, DateTimeOffset now)
    {
        if (State is DepositReturnState.Settling or DepositReturnState.Completed) return TransitionResult.Ignored;

        // Geri konduktan sonra gelen "başarılı": para hem göndericide hem askıda olabilir.
        if (State is not DepositReturnState.BankTransferPending) return TransitionResult.Conflict;

        BankReference = bankReference;
        BankFee = feeAmount;

        return Advance(DepositReturnState.Settling, now);
    }

    public TransitionResult Settled(Guid ledgerTransactionId, DateTimeOffset now)
    {
        if (State is DepositReturnState.Completed) return TransitionResult.Ignored;

        if (State is not DepositReturnState.Settling) return TransitionResult.Conflict;

        SettlementTransactionId = ledgerTransactionId;

        return Advance(DepositReturnState.Completed, now);
    }

    public TransitionResult BankTransferFailed(string reason, DateTimeOffset now)
    {
        if (State is DepositReturnState.Restoring or DepositReturnState.Failed) return TransitionResult.Ignored;

        // Muhasebesi kapanan iadeye "başarısız" demek çelişki: para gitti sayıldı.
        if (State is not DepositReturnState.BankTransferPending) return TransitionResult.Conflict;

        FailureReason = reason;
        FailureRule = DepositReturnFailureRules.BankRejected;

        return Advance(DepositReturnState.Restoring, now);
    }

    public TransitionResult Restored(Guid ledgerTransactionId, DateTimeOffset now)
    {
        if (State is DepositReturnState.Failed) return TransitionResult.Ignored;

        if (State is not DepositReturnState.Restoring) return TransitionResult.Conflict;

        RestoreTransactionId = ledgerTransactionId;

        return Advance(DepositReturnState.Failed, now);
    }

    /// <summary>Yalnızca gerçekten ilerleyen geçişte zaman ve version artıyor.</summary>
    private TransitionResult Advance(DepositReturnState next, DateTimeOffset now)
    {
        State = next;
        UpdatedAt = now;
        Version++;

        return TransitionResult.Applied;
    }
}
