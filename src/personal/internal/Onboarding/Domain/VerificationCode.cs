using System.Security.Cryptography;
using System.Text;

namespace HiWallet.Onboarding.Domain;

/// <summary>Kodu kontrol etmenin sonucu.</summary>
public enum CodeCheck
{
    Verified,

    /// <summary>Daha önce doğrulanmıştı; tekrar zararsız.</summary>
    AlreadyVerified,

    WrongCode,

    Expired,

    /// <summary>Deneme sınırı doldu; doğru kod da kabul edilmiyor.</summary>
    Locked
}

/// <summary>
/// E-postaya ve SMS'e giden altı haneli kod. Düz saklanmıyor: veritabanını okuyan biri
/// açık bir kaydı tamamlayamamalı. Özet kaydın kimliğiyle tuzlanıyor; aynı kod iki
/// kayıtta aynı özeti vermiyor.
/// </summary>
public static class VerificationCode
{
    public static string New() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Hash(Guid salt, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt:N}:{code}")));

    /// <summary>Sabit zamanlı karşılaştırma: yanıt süresi kodun kaçıncı hanede ayrıldığını söylemiyor.</summary>
    public static bool Matches(Guid salt, string code, string hash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(salt, code)),
            Encoding.ASCII.GetBytes(hash));
}
