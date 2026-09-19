using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace HiWallet.BankWebhook.Application;

/// <summary>
/// Bankanın callback imzasını doğrular: ham gövde baytları üzerinde HMAC-SHA256,
/// sabit zamanlı karşılaştırma.
///
/// <b>Ham bayt şart.</b> İmza deserialize edilip yeniden serialize edilmiş JSON
/// üzerinde hesaplanamaz: boşluk, alan sırası ve sayı biçimi değişir, imza tutmaz.
/// Controller gövdeyi bayt olarak okuyup önce doğruluyor, sonra saklıyor.
///
/// <b><c>topup-webhook</c>'un imzasıyla PAYLAŞILMIYOR</b> ve bu bilinçli. Orada
/// başlık adını ve şemayı BİZ belirledik — sağlayıcılara verdiğimiz sözleşme.
/// Burada bankanın belirlediğini uyguluyoruz. Algoritmanın aynı olması yaygın
/// kalıbın sonucu, ortak bir sözleşme olduğu anlamına gelmiyor: banka yarın
/// şemasını değiştirdiğinde yalnızca bu dosya değişmeli, top-up hattı değil.
///
/// <b>Zaman damgası yok.</b> Yakalanan bir request'in tekrar gönderilmesi bu hatta
/// zararsız: aynı <c>event_id</c> inbox'ta UNIQUE'e takılıyor. Zaman damgası
/// eklemek aynı korumayı ikinci kez, üstelik saat senkronizasyonuna bağımlı
/// biçimde kurmak olurdu.
/// </summary>
public static class BankCallbackSignature
{
    /// <summary>Bankanın kendi başlık adı. Bizim <c>X-Hive-Signature</c>'ımızla ilgisi yok.</summary>
    public const string HeaderName = "X-Bank-Signature";

    private const string Prefix = "sha256=";
    private const int HashLength = 32;

    /// <summary>
    /// Başlık geçerli mi. Biçimi bozuk, eksik ya da yanlış uzunlukta olan her şey
    /// <c>false</c> — istisna fırlatılmıyor, çünkü girdi tamamen dış dünyadan geliyor.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<byte> body, string secret, string? header)
    {
        if (string.IsNullOrEmpty(header)) return false;
        if (!header.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var hex = header.AsSpan(Prefix.Length);
        if (hex.Length != HashLength * 2) return false;

        Span<byte> provided = stackalloc byte[HashLength];
        if (Convert.FromHexString(hex, provided, out _, out var written) != OperationStatus.Done) return false;
        if (written != HashLength) return false;

        Span<byte> expected = stackalloc byte[HashLength];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body, expected);

        // Sabit zamanlı karşılaştırma: normal karşılaştırma ilk farklı baytta
        // döndüğü için, süre ölçerek imza bayt bayt tahmin edilebilir.
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
