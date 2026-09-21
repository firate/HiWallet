namespace HiWallet.TopupWebhook.Application;

/// <summary>
/// Sağlayıcı başına paylaşılan HMAC secret'ları. Konfigürasyondan okunur ama
/// <c>appsettings.json</c>'a YAZILMAZ — ortam değişkeni ya da User Secrets
/// (<c>Providers__stripe-fake__WebhookSecret</c>).
///
/// <b>Sağlayıcı başına birden fazla secret</b> tutuluyor ve sebebi rotasyon. Bu
/// hatta sözleşme bizim olduğu için değeri de biz üretiyoruz, ama sağlayıcının onu
/// kendi sistemine yayması zaman alıyor; o aralıkta gelen webhook'lar eski secret
/// ile imzalı geliyor. İki değer aynı anda geçerli olmazsa para yükleme bildirimleri
/// <c>401</c> alır ve inbox'a hiç girmez.
///
/// Sıra anlamlı: ilk secret güncel olan, sonrakiler rotasyon penceresinde açık
/// tutulanlar. Çağıran sonraki bir secret'la doğrulanan bildirimi kaydediyor —
/// o kayıt kesildiğinde eskisini kaldırmak güvenli olur.
///
/// Geçersiz kılma ayrı bir mekanizma DEĞİL: secret listeden çıkarılır
/// (bank-webhook ile aynı karar, decisions.md madde 35).
/// </summary>
public sealed class WebhookSecrets(IReadOnlyDictionary<string, IReadOnlyList<string>> secretsByProvider)
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
    public bool TryGet(string provider, out IReadOnlyList<string> secrets) =>
        secretsByProvider.TryGetValue(provider, out secrets!);
}
