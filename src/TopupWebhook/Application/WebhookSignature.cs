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
/// <b>Zaman damgası yok.</b> Yakalanan bir isteğin tekrar gönderilmesi (replay) bu
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
