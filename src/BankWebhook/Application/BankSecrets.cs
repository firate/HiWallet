namespace HiWallet.BankWebhook.Application;

/// <summary>
/// Kurum başına paylaşılan HMAC secret'ları. Konfigürasyondan okunur ama
/// <c>appsettings.json</c>'a YAZILMAZ — ortam değişkeni ya da User Secrets
/// (<c>Banks__bank-fake__CallbackSecret</c>).
///
/// <b>Kurum başına birden fazla secret</b> tutuluyor ve sebebi rotasyon. Bankaya
/// yeni secret iletildikten sonra karşı tarafın geçişi ne zaman tamamlayacağını biz
/// belirlemiyoruz; o aralıkta iki secret de geçerli olmak zorunda. Eskisini erken
/// kaldırmak callback'leri <c>401</c>'e düşürür, <c>bank_transfers</c> satırları
/// <c>pending</c> kalır ve sonuç ancak mutabakat taramasıyla öğrenilir.
/// Geçersiz kılma da ayrı bir mekanizma DEĞİL: secret listeden çıkarılır.
///
/// Sıra anlamlı: ilk secret güncel olan, sonrakiler rotasyon penceresinde açık
/// tutulanlar. Çağıran sonraki bir secret'la doğrulanan callback'i kaydediyor —
/// o kayıt kesildiğinde eskisini kaldırmak güvenli olur.
///
/// <c>topup-webhook</c>'un <c>Providers</c> bölümünden AYRI bir bölüm
/// (<c>Banks</c>): oradakiler bizim sağlayıcılarımız, buradaki banka. Aynı bölümü
/// paylaşsalardı iki servisin secret'ları tek bir yerde toplanır ve bir servisin
/// sızması diğerini de açardı.
/// </summary>
public sealed class BankSecrets(IReadOnlyDictionary<string, IReadOnlyList<string>> secretsByBank)
{
    public const string SectionName = "Banks";

    /// <summary>
    /// Tanımlı kurumlar. Yalnızca teşhis ve fail-fast için; secret'lar dışarı
    /// verilmiyor.
    /// </summary>
    public IReadOnlyCollection<string> Banks => (IReadOnlyCollection<string>)secretsByBank.Keys;

    /// <summary>
    /// Kurum tanınmıyorsa <c>false</c>. Çağıran bunu <c>401</c>'e çeviriyor,
    /// <c>404</c>'e değil: hangi bankalarla çalıştığımız dışarıya sızmamalı.
    /// </summary>
    public bool TryGet(string bank, out IReadOnlyList<string> secrets) =>
        secretsByBank.TryGetValue(bank, out secrets!);
}
