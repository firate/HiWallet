namespace HiWallet.TopupWebhook.Api.Requests;

/// <summary>
/// Kart sağlayıcısının ödeme bildirimi. Tamamen dış dünyanın şekli — iç sözleşme
/// <c>CardPaymentUpdated</c> ayrı duruyor ve bu tip ona çevriliyor.
///
/// Tüm alanlar nullable, BİLEREK. Non-nullable olsalardı eksik bir alan sessizce
/// varsayılana düşerdi (<c>amount</c> yoksa 0) ve doğrulama "eksik" yerine "geçersiz"
/// derdi. Nullable olunca "gönderilmedi" ile "0 gönderildi" ayrışıyor.
/// </summary>
public sealed class TopupWebhookPayload
{
    public string? EventId { get; init; }

    /// <summary><c>payment.succeeded</c> ya da <c>payment.canceled</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Sağlayıcının ödeme kimliği.</summary>
    public string? PaymentId { get; init; }

    /// <summary>Ödemeyi açarken verdiğimiz kimlik: kartla yüklemenin kimliği.</summary>
    public Guid? Reference { get; init; }

    public decimal? Amount { get; init; }

    public string? Currency { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }
}
