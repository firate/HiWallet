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
    /// müşterinin aynı key'i üretmesi isteklerini karıştırırdı (decisions.md madde 4).
    /// </summary>
    public Guid AccountId { get; private set; }

    /// <summary>Parası düşülecek cüzdan.</summary>
    public Guid WalletId { get; private set; }

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

    /// <summary>Reddetme ya da banka hatasının sebebi. Müşteriye gösterilebilir.</summary>
    public string? FailureReason { get; private set; }

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

        return new WithdrawalSaga
        {
            Id = id,
            AccountId = accountId,
            WalletId = walletId,
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
    public TransitionResult Rejected(string reason, DateTimeOffset now)
    {
        if (State is WithdrawalState.Rejected) return TransitionResult.Ignored;

        // Para düştükten sonra "reddedildi" demek anlamsız: geri alınacak bir hareket
        // var ve doğru yol telafi, reddetme değil.
        if (State is not WithdrawalState.Initiated) return TransitionResult.Conflict;

        FailureReason = reason;

        return Advance(WithdrawalState.Rejected, now);
    }

    public TransitionResult Debited(Guid ledgerTransactionId, decimal totalDebited, DateTimeOffset now)
    {
        // Saga bu adımı geçmiş: tekrar gelen event geri sarmamalı.
        if (State is WithdrawalState.Debited
            or WithdrawalState.BankTransferPending
            or WithdrawalState.Completed
            or WithdrawalState.Compensating
            or WithdrawalState.Failed)
        {
            return TransitionResult.Ignored;
        }

        // Reddedilmiş saga'da wallet'a hiç komut gitmedi; cevap gelmesi mümkün değil.
        if (State is not WithdrawalState.Initiated) return TransitionResult.Conflict;

        DebitTransactionId = ledgerTransactionId;
        TotalDebited = totalDebited;

        return Advance(WithdrawalState.Debited, now);
    }

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

        return Advance(WithdrawalState.Compensating, now);
    }

    public TransitionResult Refunded(Guid ledgerTransactionId, DateTimeOffset now)
    {
        if (State is WithdrawalState.Failed) return TransitionResult.Ignored;

        if (State is not WithdrawalState.Compensating) return TransitionResult.Conflict;

        RefundTransactionId = ledgerTransactionId;

        return Advance(WithdrawalState.Failed, now);
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
