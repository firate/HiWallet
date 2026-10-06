namespace HiWallet.Stripe.Fake.Webhooks;

/// <summary>Sahte sağlayıcının adresleri ve imza anahtarı.</summary>
public sealed class StripeFakeOptions
{
    public const string SectionName = "StripeFake";

    /// <summary>
    /// Sağlayıcı kimliği. Konfigürasyon DEĞİL: servis tek bir kurumu temsil ediyor ve yanlış
    /// konfigürasyonla başka bir kurum gibi davranması ledger'da yanlış clearing hesabına
    /// yazdırırdı.
    /// </summary>
    public const string Provider = "stripe-fake";

    /// <summary><c>topup-webhook</c>'un adresi, örnek: <c>http://topup-webhook:8080</c>.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>
    /// Paylaşılan HMAC anahtarı. <c>topup-webhook</c>'taki <c>Providers:stripe-fake:WebhookSecret</c>
    /// ile AYNI olmak zorunda; ayrışırsa her webhook <c>401</c> alır.
    /// </summary>
    public string? WebhookSecret { get; init; }

    /// <summary>
    /// Ödeme sayfasının tarayıcıdan ulaşılan adresi. Müşteri buraya yönlendiriliyor; iç ağdaki
    /// adres değil.
    /// </summary>
    public string? PublicUrl { get; init; }
}
