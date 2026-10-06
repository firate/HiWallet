namespace HiWallet.Stripe.Fake.Payments;

/// <summary>Sağlayıcıdaki ödemenin durumu, gerçek bir sağlayıcının API'sindeki gibi.</summary>
public enum PaymentStatus
{
    /// <summary>Açık: müşteri kartını girip onaylamadı.</summary>
    RequiresPayment,

    /// <summary>Kart çekildi.</summary>
    Succeeded,

    /// <summary>Müşteri vazgeçti.</summary>
    Canceled,

    /// <summary>Oturumun süresi doldu, ödeme yapılmadı.</summary>
    Expired
}

/// <summary>
/// Sağlayıcıda açılmış ödeme: bir kart çekimi oturumu. Bellekte; süreç yeniden başlayınca
/// silinir (bankanın sahtesindeki gibi).
/// </summary>
public sealed class CardPayment
{
    public required string Id { get; init; }

    /// <summary>Ödemeyi açanın kendi kimliği; sağlayıcı aynı referansla ikinci ödeme açmıyor.</summary>
    public required Guid Reference { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Müşterinin ödemeden sonra döneceği adres.</summary>
    public required string ReturnUrl { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Müşterinin kararı; süresi dolmuşluk okunurken hesaplanıyor.</summary>
    public PaymentStatus Decision { get; set; } = PaymentStatus.RequiresPayment;

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>
    /// Okunan an itibarıyla durum. Süresi dolan açık ödeme <see cref="PaymentStatus.Expired"/>:
    /// gerçek sağlayıcı da oturumu kapatıyor ve sonradan ödeme kabul etmiyor.
    /// </summary>
    public PaymentStatus StatusAt(DateTimeOffset now) =>
        Decision is PaymentStatus.RequiresPayment && now >= ExpiresAt ? PaymentStatus.Expired : Decision;
}
