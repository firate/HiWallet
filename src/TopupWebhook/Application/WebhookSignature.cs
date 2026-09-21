using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace HiWallet.TopupWebhook.Application;

/// <summary>
/// Webhook imzası: gövdenin ham baytları üzerinde HMAC-SHA256, paylaşılan sağlayıcı
/// secret'ı ile. Sahte webhook'u engelliyor (overview.md madde 5).
///
/// <b>Ham bayt önemli.</b> İmza deserialize edilip yeniden serialize edilmiş JSON
/// üzerinde hesaplanamaz: boşluk, alan sırası, sayı biçimi değişir ve imza tutmaz.
/// Bu yüzden controller gövdeyi bayt olarak okuyup önce doğruluyor, sonra parse ediyor.
///
/// <b>Zaman damgası yok.</b> Yakalanan bir request'in tekrar gönderilmesi (replay) bu
/// hatta zaten zararsız: aynı <c>event_id</c> inbox'ta UNIQUE'e takılıyor ve ikinci
/// kez işlenmiyor. Zaman damgası eklemek aynı korumayı ikinci kez, üstelik saat
/// senkronizasyonuna bağımlı biçimde kurmak olurdu.
/// </summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-Hive-Signature";

    private const string Prefix = "sha256=";
    private const int HashLength = 32;

    public static string Compute(ReadOnlySpan<byte> body, string secret)
    {
        Span<byte> hash = stackalloc byte[HashLength];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body, hash);

        return Prefix + Convert.ToHexStringLower(hash);
    }

    /// <summary>Hiçbir secret tutmadı.</summary>
    public const int NoMatch = -1;

    /// <summary>
    /// İmzayı sıradaki her secret'la deneyip TUTANIN sırasını döner, hiçbiri
    /// tutmazsa <see cref="NoMatch"/>. Biçimi bozuk, eksik ya da yanlış uzunlukta
    /// olan her başlık da <see cref="NoMatch"/> — istisna fırlatılmıyor, çünkü girdi
    /// tamamen dış dünyadan geliyor.
    ///
    /// <b>Neden birden fazla secret.</b> Rotasyon penceresinde sağlayıcı hâlâ
    /// eskisiyle imzalıyor olabilir (<see cref="WebhookSecrets"/>). Sıra geri
    /// dönülüyor ki çağıran eski secret'ın hâlâ kullanıldığını kaydedebilsin.
    ///
    /// <b>İlk tutanda çıkmak imzayı zayıflatmıyor.</b> Sabit zamanlı karşılaştırmanın
    /// koruduğu şey imzanın bayt bayt tahmin edilmesi ve her karşılaştırma ayrı ayrı
    /// sabit zamanlı. Erken çıkış yalnızca kaçıncı secret'ın tuttuğunu sızdırır;
    /// o bilgiye ulaşmak için elde zaten geçerli bir imza olması gerekir.
    /// </summary>
    public static int Match(ReadOnlySpan<byte> body, IReadOnlyList<string> secrets, string? header)
    {
        if (string.IsNullOrEmpty(header)) return NoMatch;
        if (!header.StartsWith(Prefix, StringComparison.Ordinal)) return NoMatch;

        var hex = header.AsSpan(Prefix.Length);
        if (hex.Length != HashLength * 2) return NoMatch;

        Span<byte> provided = stackalloc byte[HashLength];
        if (Convert.FromHexString(hex, provided, out _, out var written) != OperationStatus.Done) return NoMatch;
        if (written != HashLength) return NoMatch;

        // Başlık çözümü secret'tan bağımsız, bu yüzden döngünün dışında. stackalloc
        // da döngüye giremez: her turda stack büyürdü.
        Span<byte> expected = stackalloc byte[HashLength];

        for (var i = 0; i < secrets.Count; i++)
        {
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secrets[i]), body, expected);

            // Sabit zamanlı karşılaştırma: normal karşılaştırma ilk farklı baytta
            // döndüğü için, süre ölçerek imza bayt bayt tahmin edilebilir.
            if (CryptographicOperations.FixedTimeEquals(expected, provided)) return i;
        }

        return NoMatch;
    }
}
