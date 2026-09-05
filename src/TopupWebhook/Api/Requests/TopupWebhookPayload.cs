namespace HiWallet.TopupWebhook.Api.Requests;

/// <summary>
/// Sağlayıcının gönderdiği gövde. Tamamen dış dünyanın şekli — iç sözleşme
/// <c>TopupReceived</c> ayrı duruyor ve bu tip ona çevriliyor.
///
/// Tüm alanlar nullable, BİLEREK. Non-nullable olsalardı eksik bir alan sessizce
/// varsayılana düşerdi (<c>amount</c> yoksa 0) ve doğrulama "eksik" yerine "geçersiz"
/// derdi. Nullable olunca "gönderilmedi" ile "0 gönderildi" ayrışıyor.
/// </summary>
public sealed class TopupWebhookPayload
{
    public string? EventId { get; init; }

    public Guid? WalletId { get; init; }

    public decimal? Amount { get; init; }

    public string? Currency { get; init; }

    /// <summary>Sağlayıcının kendi ödeme referansı.</summary>
    public string? Reference { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }
}
