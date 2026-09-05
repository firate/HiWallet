namespace HiWallet.TopupWebhook.Application;

/// <summary>
/// Sağlayıcı başına paylaşılan HMAC secret'ı. Konfigürasyondan okunur ama
/// <c>appsettings.json</c>'a YAZILMAZ — ortam değişkeni ya da User Secrets
/// (<c>Providers__stripe-fake__WebhookSecret</c>).
/// </summary>
public sealed class WebhookSecrets(IReadOnlyDictionary<string, string> secretsByProvider)
{
    public const string SectionName = "Providers";

    /// <summary>
    /// Tanımlı sağlayıcılar. Yalnızca teşhis ve fail-fast için; secret'lar dışarı
    /// verilmiyor.
    /// </summary>
    public IReadOnlyCollection<string> Providers => (IReadOnlyCollection<string>)secretsByProvider.Keys;

    /// <summary>
    /// Sağlayıcı tanınmıyorsa <c>false</c>. Çağıran bunu 401'e çeviriyor, 404'e
    /// değil: hangi sağlayıcıların tanımlı olduğu dışarıya sızmamalı.
    /// </summary>
    public bool TryGet(string provider, out string secret) =>
        secretsByProvider.TryGetValue(provider, out secret!);
}
