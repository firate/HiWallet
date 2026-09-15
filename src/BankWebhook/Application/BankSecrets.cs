namespace HiWallet.BankWebhook.Application;

/// <summary>
/// Kurum başına paylaşılan HMAC secret'ı. Konfigürasyondan okunur ama
/// <c>appsettings.json</c>'a YAZILMAZ — ortam değişkeni ya da User Secrets
/// (<c>Banks__bank-fake__CallbackSecret</c>).
///
/// <c>topup-webhook</c>'un <c>Providers</c> bölümünden AYRI bir bölüm
/// (<c>Banks</c>): oradakiler bizim sağlayıcılarımız, buradaki banka. Aynı bölümü
/// paylaşsalardı iki servisin secret'ları tek bir yerde toplanır ve bir servisin
/// sızması diğerini de açardı.
/// </summary>
public sealed class BankSecrets(IReadOnlyDictionary<string, string> secretsByBank)
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
    public bool TryGet(string bank, out string secret) =>
        secretsByBank.TryGetValue(bank, out secret!);
}
