using HiWallet.Shared.Contracts.Actors;

namespace HiWallet.WithdrawalOrchestrator.Domain;

/// <summary>
/// Para çekme saga'sının durumu ve geçiş kuralları (overview.md madde 6).
///
/// Saf: DB, mesajlaşma ve zaman bilmiyor — "şimdi"yi çağıran veriyor. Bütün geçiş
/// kuralları burada tek yerde okunabiliyor, akışın nerede olduğunu anlamak için
/// handler'ları dolaşmak gerekmiyor.
///
/// <b>Wallet'ın para tiplerini KULLANMIYOR.</b> Tutar burada <c>decimal</c> +
/// <c>string currency</c>; <c>Money</c> ve <c>Currency</c> wallet sınırının içinde
/// (CLAUDE.md "Servis sınırı"). Orchestrator ledger matematiği yapmıyor, yalnızca
/// komut gönderip sonucu kaydediyor.
/// </summary>
public sealed class WithdrawalSaga
{
    private WithdrawalSaga()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Idempotency kapsamı. Key'i client üretiyor; yalnız key UNIQUE olsaydı iki
    /// müşterinin aynı key'i üretmesi request'lerini karıştırırdı (decisions.md madde 4).
    /// </summary>
    public Guid AccountId { get; private set; }

    /// <summary>Parası düşülecek cüzdan.</summary>
    public Guid WalletId { get; private set; }

    /// <summary>
    /// Çekimi kim başlattı (decisions.md madde 34). Normalde hesabın sahibi;
    /// backoffice müşteri adına çekim açtığında o çalışan.
    ///
    /// <c>AccountId</c>'den TÜRETİLEMİYOR: hesap "parası kimin" sorusunu, bu alan
    /// "kim istedi" sorusunu cevaplıyor ve ikisi her zaman aynı kişi değil.
    /// </summary>
    public string InitiatedByType { get; private set; } = string.Empty;

    public string InitiatedById { get; private set; } = string.Empty;

    /// <summary>
    /// İsteyen kimlik: kimlik sağlayıcıdaki kullanıcının <c>sub</c>'ı. Müşteri çekiminde
    /// dolu. Orchestrator hesabın kullanıcılarını bilmiyor; kimliği düşme komutuyla
    /// wallet'a taşıyor ve üyeliği wallet doğruluyor.
    /// </summary>
    public string? InitiatedBySubject { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Müşterinin çekmek istediği tutar. Komisyon HARİÇ.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public Iban Destination { get; private set; }

    public WithdrawalState State { get; private set; }

    /// <summary>
    /// Cüzdandan gerçekte çıkan toplam (tutar + komisyon). Komisyonu wallet
    /// hesaplıyor ve sonucu bildiriyor — politikayı iki serviste tekrarlamamak için.
    /// </summary>
    public decimal? TotalDebited { get; private set; }

    /// <summary>Düşme işleminin ledger kaydı. Reddedilen saga'da NULL kalır.</summary>
    public Guid? DebitTransactionId { get; private set; }

    /// <summary>Ters kaydın ledger işlemi. Yalnızca telafi yolunda dolar.</summary>
    public Guid? RefundTransactionId { get; private set; }

    /// <summary>Bankaya gönderilen komutun kimliği; bankanın idempotency anahtarı.</summary>
    public Guid? BankCommandId { get; private set; }

    /// <summary>Bankanın referansı; settlement komutunda ve mutabakatta kullanılıyor.</summary>
    public string? BankReference { get; private set; }

    /// <summary>Bankanın kestiği ücret. Transfer başarılı olana kadar NULL.</summary>
    public decimal? BankFee { get; private set; }

    /// <summary>Settlement kaydının ledger işlemi. Muhasebe kapanana kadar NULL.</summary>
    public Guid? SettlementTransactionId { get; private set; }

    /// <summary>
    /// Reddetme, banka hatası ya da iptalin sebebinin metni: log ve destek için, iç ayrıntı
    /// taşıyabilir. Müşteriye <see cref="FailureRule"/> gösteriliyor.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Sebebin makinenin okuyacağı adı. Reddetmede wallet'ın kuralı (<c>insufficient_funds</c>,
    /// <c>Kyc.Withdrawal.Monthly</c>, ...), banka hatasında ve iptalde
    /// <see cref="WithdrawalFailureRules"/>. Alan sonradan eklenmeden önce kapanan saga'da yok.
    /// </summary>
    public string? FailureRule { get; private set; }

    /// <summary>
    /// İncelemede karar veren çalışanın <c>sub</c>'ı (serbest bıraktı ya da iptal etti).
    /// Serbest bırakma ledger'a yazmıyor; kararı veren kalıcı olarak burada duruyor
    /// (decisions.md madde 34). İptalde ters kaydın aktörü de o.
    /// </summary>
    public string? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Optimistic lock. Aynı saga'ya iki event aynı anda gelebiliyor (banka cevabı
    /// ile stuck-saga taraması gibi); ikisinin birbirinin üstüne yazmaması gerekiyor.
    /// </summary>
    public long Version { get; private set; }

    public bool IsTerminal => State.IsTerminal();

    public static WithdrawalSaga Start(
        Guid id,
        Guid accountId,
        Guid walletId,
        decimal amount,
        string currency,
        Iban destination,
        string idempotencyKey,
        CommandActor initiatedBy,
        DateTimeOffset startedAt)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("Çekim tutarı pozitif olmalı.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key zorunlu.", nameof(idempotencyKey));
        }

        ArgumentNullException.ThrowIfNull(initiatedBy);

        if (string.IsNullOrWhiteSpace(initiatedBy.Id))
        {
            throw new ArgumentException("Çekimi başlatan belirtilmeden saga açılamaz.", nameof(initiatedBy));
        }

        if (initiatedBy.Type == ActorTypes.Customer && string.IsNullOrWhiteSpace(initiatedBy.Subject))
        {
            throw new ArgumentException("Müşteri çekimi isteyen kimlik olmadan açılamaz.", nameof(initiatedBy));
        }

        return new WithdrawalSaga
        {
            Id = id,
            AccountId = accountId,
            WalletId = walletId,
            InitiatedByType = initiatedBy.Type,
            InitiatedById = initiatedBy.Id,
            InitiatedBySubject = initiatedBy.Subject,
            Amount = amount,
            Currency = currency,
            Destination = destination,
            IdempotencyKey = idempotencyKey,
            State = WithdrawalState.Initiated,
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
            Version = 0
        };
    }

    /// <summary>Kural/limit reddi. Yalnızca hiç para hareketi olmamışken geçerli.</summary>
    /// <param name="rule">Wallet'ın kural adı; kuralsız eski mesajda <c>null</c>.</param>
    public TransitionResult Rejected(string reason, string? rule, DateTimeOffset now)
    {
        if (State is WithdrawalState.Rejected) return TransitionResult.Ignored;

        // Para düştükten sonra "reddedildi" demek anlamsız: geri alınacak bir hareket
        // var ve doğru yol telafi, reddetme değil.
        if (State is not WithdrawalState.Initiated) return TransitionResult.Conflict;

        FailureReason = reason;
        FailureRule = rule;

        return Advance(WithdrawalState.Rejected, now);
    }

    public TransitionResult Debited(Guid ledgerTransactionId, decimal totalDebited, DateTimeOffset now)
    {
        // Saga bu adımı geçmiş: tekrar gelen event geri sarmamalı.
        if (State is WithdrawalState.Debited
            or WithdrawalState.UnderReview
            or WithdrawalState.BankTransferPending
            or WithdrawalState.Settling
            or WithdrawalState.Completed
            or WithdrawalState.Compensating
            or WithdrawalState.Failed
            or WithdrawalState.Cancelling
            or WithdrawalState.Cancelled)
        {
            return TransitionResult.Ignored;
        }

        // Reddedilmiş saga'da wallet'a hiç komut gitmedi; cevap gelmesi mümkün değil.
        if (State is not WithdrawalState.Initiated) return TransitionResult.Conflict;

        DebitTransactionId = ledgerTransactionId;
        TotalDebited = totalDebited;

        return Advance(WithdrawalState.Debited, now);
    }

    /// <summary>
    /// Tutar inceleme eşiğinin üstünde: düşülen çekim bankaya gitmeden bekliyor. Eşiği
    /// çağıran biliyor; saga ayar okumuyor.
    /// </summary>
    public TransitionResult HoldForReview(DateTimeOffset now)
    {
        if (State is not WithdrawalState.Debited)
        {
            return State is WithdrawalState.Initiated or WithdrawalState.Rejected
                ? TransitionResult.Conflict
                : TransitionResult.Ignored;
        }

        return Advance(WithdrawalState.UnderReview, now);
    }

    /// <summary>
    /// Çalışan incelemede serbest bıraktı: banka komutu gidiyor. Komutun kimliği
    /// saga'ya aynı geçişte yazılıyor (<see cref="BankTransferStarted"/> ile aynı gerekçe).
    /// </summary>
    public TransitionResult Release(string reviewer, Guid bankCommandId, DateTimeOffset now)
    {
        // Karar bir kez veriliyor: iptal edilmiş çekim serbest bırakılamaz, serbest
        // bırakılmış çekimde ikinci karar bir çelişki.
        if (State is not WithdrawalState.UnderReview) return TransitionResult.Conflict;

        ReviewedBy = reviewer;
        ReviewedAt = now;
        BankCommandId = bankCommandId;

        return Advance(WithdrawalState.BankTransferPending, now);
    }

    /// <summary>
    /// Çalışan incelemede iptal etti: para cüzdana geri veriliyor. Ters kaydın aktörü
    /// iptal eden çalışan; müşteri iadeyi istemedi, banka da reddetmedi.
    /// </summary>
    public TransitionResult Cancel(string reviewer, string reason, DateTimeOffset now)
    {
        if (State is not WithdrawalState.UnderReview) return TransitionResult.Conflict;

        ReviewedBy = reviewer;
        ReviewedAt = now;
        FailureReason = reason;
        FailureRule = WithdrawalFailureRules.ReviewCancelled;

        return Advance(WithdrawalState.Cancelling, now);
    }

    /// <summary>İptali yapan çalışan; ters kayıt komutunun aktörü.</summary>
    public CommandActor ReviewerActor() =>
        new() { Type = ActorTypes.Employee, Id = ReviewedBy ?? throw new InvalidOperationException("Çekim incelenmedi.") };

    public TransitionResult BankTransferStarted(Guid commandId, DateTimeOffset now)
    {
        if (State is WithdrawalState.BankTransferPending
            or WithdrawalState.Completed
            or WithdrawalState.Compensating
            or WithdrawalState.Failed)
        {
            return TransitionResult.Ignored;
        }

        if (State is not WithdrawalState.Debited) return TransitionResult.Conflict;

        BankCommandId = commandId;

        return Advance(WithdrawalState.BankTransferPending, now);
    }

    /// <param name="feeAmount">
    /// Bankanın kestiği ücret. Saga'da SAKLANIYOR çünkü settlement komutu bu
    /// bilgiyi taşıyor ve komut yeniden gönderilebilmeli — event'i tekrar beklemek
    /// gerekseydi kayıp bir event saga'yı kalıcı olarak asardı.
    /// </param>
    public TransitionResult BankTransferSucceeded(
        string bankReference, decimal feeAmount, DateTimeOffset now)
    {
        if (State is WithdrawalState.Settling or WithdrawalState.Completed)
        {
            return TransitionResult.Ignored;
        }

        // Telafi başladıktan sonra gelen "başarılı" ÇELİŞKİ, tekrar değil: para hem
        // bankadan çıkmış hem müşteriye iade edilmiş olabilir. Yok saymak zararı
        // görünmez kılardı — durum olduğu yerde bırakılıp alarm üretiliyor.
        if (State is not WithdrawalState.BankTransferPending) return TransitionResult.Conflict;

        BankReference = bankReference;
        BankFee = feeAmount;

        return Advance(WithdrawalState.Settling, now);
    }

    /// <summary>
    /// Muhasebe kapandı. Saga'nın mutlu yoldaki terminal geçişi.
    /// </summary>
    public TransitionResult Settled(Guid ledgerTransactionId, DateTimeOffset now)
    {
        if (State is WithdrawalState.Completed) return TransitionResult.Ignored;

        if (State is not WithdrawalState.Settling) return TransitionResult.Conflict;

        SettlementTransactionId = ledgerTransactionId;

        return Advance(WithdrawalState.Completed, now);
    }

    public TransitionResult BankTransferFailed(string reason, DateTimeOffset now)
    {
        if (State is WithdrawalState.Compensating or WithdrawalState.Failed)
        {
            return TransitionResult.Ignored;
        }

        // Tamamlanmış ya da muhasebesi kapanan bir saga'ya "başarısız" demek çelişki:
        // para gitti sayıldı ve settling'de clearing boşaltılıyor.
        if (State is not WithdrawalState.BankTransferPending) return TransitionResult.Conflict;

        FailureReason = reason;
        FailureRule = WithdrawalFailureRules.BankRejected;

        return Advance(WithdrawalState.Compensating, now);
    }

    /// <summary>
    /// Saga'nın kendi ürettiği komutların aktörü. Bunlar bir tepki: iadeyi bankanın
    /// reddi, settlement'ı bankanın bildirimi tetikliyor. Başlatan bir insan yok,
    /// o yüzden çekimi başlatan kişiye DE yazılmıyor — müşteri iadeyi istemedi.
    /// </summary>
    public static CommandActor SagaActor { get; } = new()
    {
        Type = ActorTypes.System,
        Id = SystemFlows.WithdrawalSaga
    };

    /// <summary>Çekimi başlatanın mesaja konulabilir hali.</summary>
    public CommandActor InitiatedBy() =>
        new() { Type = InitiatedByType, Id = InitiatedById, Subject = InitiatedBySubject };

    /// <summary>
    /// Ters kayıt yazıldı. Banka reddinin telafisi <see cref="WithdrawalState.Failed"/>'da,
    /// çalışanın iptali <see cref="WithdrawalState.Cancelled"/>'da bitiyor.
    /// </summary>
    public TransitionResult Refunded(Guid ledgerTransactionId, DateTimeOffset now)
    {
        if (State is WithdrawalState.Failed or WithdrawalState.Cancelled) return TransitionResult.Ignored;

        var next = State switch
        {
            WithdrawalState.Compensating => WithdrawalState.Failed,
            WithdrawalState.Cancelling => WithdrawalState.Cancelled,
            _ => (WithdrawalState?)null
        };

        if (next is null) return TransitionResult.Conflict;

        RefundTransactionId = ledgerTransactionId;

        return Advance(next.Value, now);
    }

    /// <summary>
    /// Yalnızca GERÇEKTEN ilerleyen geçişte zaman damgası ve version artıyor.
    /// Yok sayılan event dokunmuyor; aksi halde "en son ne zaman ilerledi" bilgisi
    /// bozulur ve stuck saga taraması takılmış saga'ları canlı sanır.
    /// </summary>
    private TransitionResult Advance(WithdrawalState next, DateTimeOffset now)
    {
        State = next;
        UpdatedAt = now;
        Version++;

        return TransitionResult.Applied;
    }
}
