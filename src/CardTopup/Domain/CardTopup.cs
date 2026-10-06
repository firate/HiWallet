namespace HiWallet.CardTopup.Domain;

/// <summary>
/// Kartla yükleme ve geçiş kuralları. Saf: DB, mesajlaşma ve zaman bilmiyor — "şimdi"yi
/// çağıran veriyor (çekim saga'sının kalıbı).
///
/// <b>Kimliği üç yerde aynı:</b> buradaki kayıt, wallet'taki limit payı ve sağlayıcıya
/// verilen ödeme referansı. Sağlayıcının bildirimi bu kimliği geri getiriyor.
///
/// Wallet'ın para tiplerini KULLANMIYOR: tutar <c>decimal</c> + <c>string currency</c>.
/// Limit ve ledger wallet'ın bilgisi.
/// </summary>
public sealed class CardTopup
{
    private CardTopup()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Yüklemeyi başlatan kimlik (<c>sub</c>). Idempotency kapsamı da bu: anahtarı istemci
    /// üretiyor ve iki müşterinin aynı anahtarı üretmesi istekleri karıştırmamalı.
    /// </summary>
    public string Subject { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public Guid WalletId { get; private set; }

    /// <summary>Cüzdanın hesabı; wallet payı verdiğinde öğreniliyor.</summary>
    public Guid? AccountId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    /// <summary>Kart sağlayıcısı; <c>ledger_accounts.provider</c> ile aynı değer.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>Ödeme sayfasından sonra müşterinin döneceği adres.</summary>
    public string ReturnUrl { get; private set; } = string.Empty;

    public CardTopupState State { get; private set; }

    /// <summary>Sağlayıcıdaki ödeme kimliği; ödeme açılana kadar yok.</summary>
    public string? PaymentId { get; private set; }

    /// <summary>Müşterinin kartını gireceği sayfa; ödeme açılana kadar yok.</summary>
    public string? PaymentUrl { get; private set; }

    /// <summary>Ödeme oturumunun kapandığı an; sağlayıcıya bu süre veriliyor.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Ödenmediyse ya da reddedildiyse sebep. Müşteriye gösterilebilir.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Optimistic lock. Aynı yüklemeye sağlayıcının bildirimi ile tarama aynı anda gelebiliyor;
    /// ikisinin birbirinin üstüne yazmaması gerekiyor.
    /// </summary>
    public long Version { get; private set; }

    public bool IsTerminal => State.IsTerminal();

    public static CardTopup Start(
        Guid id,
        string subject,
        string idempotencyKey,
        Guid walletId,
        decimal amount,
        string currency,
        string provider,
        string returnUrl,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        if (amount <= 0m)
        {
            throw new ArgumentException("Tutar pozitif olmalı.", nameof(amount));
        }

        if (expiresAt <= now)
        {
            throw new ArgumentException("Ödeme oturumunun bitişi gelecekte olmalı.", nameof(expiresAt));
        }

        return new CardTopup
        {
            Id = id,
            Subject = subject,
            IdempotencyKey = idempotencyKey,
            WalletId = walletId,
            Amount = amount,
            Currency = currency,
            Provider = provider,
            ReturnUrl = returnUrl,
            State = CardTopupState.Created,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Wallet payı verdi.</summary>
    public TransitionResult HoldPlaced(Guid accountId, DateTimeOffset now)
    {
        if (State is not CardTopupState.Created)
        {
            return AccountId == accountId ? TransitionResult.Ignored : TransitionResult.Conflict;
        }

        AccountId = accountId;
        return Move(CardTopupState.Pending, now);
    }

    /// <summary>Wallet payı vermedi; ödeme açılmayacak.</summary>
    public TransitionResult HoldRejected(string reason, DateTimeOffset now)
    {
        if (State is CardTopupState.Rejected) return TransitionResult.Ignored;
        if (State is not CardTopupState.Created) return TransitionResult.Conflict;

        FailureReason = reason;
        return Move(CardTopupState.Rejected, now);
    }

    /// <summary>Sağlayıcıda ödeme açıldı.</summary>
    public TransitionResult PaymentOpened(string paymentId, string paymentUrl, DateTimeOffset now)
    {
        if (State is not CardTopupState.Pending) return TransitionResult.Conflict;

        if (PaymentId is not null)
        {
            return PaymentId == paymentId ? TransitionResult.Ignored : TransitionResult.Conflict;
        }

        PaymentId = paymentId;
        PaymentUrl = paymentUrl;
        Touch(now);
        return TransitionResult.Applied;
    }

    /// <summary>
    /// Kart çekildi. Tutar ve para birimi kayıtla aynı olmak zorunda: farklıysa sağlayıcı
    /// başka bir şey çekti. Pay serbest bırakıldıktan sonra gelen ödeme çelişki: para
    /// sağlayıcıda, limit onu artık saymıyor.
    /// </summary>
    public TransitionResult Paid(string paymentId, decimal amount, string currency, DateTimeOffset now)
    {
        if (amount != Amount || currency != Currency) return TransitionResult.Conflict;
        if (PaymentId is not null && PaymentId != paymentId) return TransitionResult.Conflict;

        if (State is CardTopupState.Paid) return TransitionResult.Ignored;
        if (State is not CardTopupState.Pending) return TransitionResult.Conflict;

        PaymentId = paymentId;
        PaymentUrl = null;
        return Move(CardTopupState.Paid, now);
    }

    /// <summary>
    /// Ödeme olmadı. Payı alınmamış (<see cref="CardTopupState.Created"/>) yükleme de bu yoldan
    /// kapanıyor: pay isteği cevapsız kaldıysa payın yazılıp yazılmadığı bilinmiyor ve
    /// ödenmedi kapanışı wallet'ta iki durumda da doğru sonucu veriyor.
    /// </summary>
    public TransitionResult Failed(string reason, DateTimeOffset now)
    {
        if (State is CardTopupState.Failed) return TransitionResult.Ignored;
        if (State is not (CardTopupState.Created or CardTopupState.Pending)) return TransitionResult.Conflict;

        FailureReason = reason;
        PaymentUrl = null;
        return Move(CardTopupState.Failed, now);
    }

    private TransitionResult Move(CardTopupState next, DateTimeOffset now)
    {
        State = next;
        Touch(now);
        return TransitionResult.Applied;
    }

    /// <summary>
    /// Her değişiklik sürümü artırıyor: token DB'de üretilmiyor, retry'da geçişin yeni durum
    /// üstünde yeniden değerlendirilmesi gerekiyor (çekim saga'sındaki gibi).
    /// </summary>
    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
