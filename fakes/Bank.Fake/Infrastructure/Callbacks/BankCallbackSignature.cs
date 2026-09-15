using System.Security.Cryptography;
using System.Text;

namespace HiWallet.Bank.Fake.Infrastructure.Callbacks;

/// <summary>
/// Bankanın callback gövdesini imzalama şekli: ham gövde baytları üzerinde
/// HMAC-SHA256, hex, <c>sha256=</c> önekiyle.
///
/// <b>Bu kod bizim doğrulayıcımızla PAYLAŞILMIYOR, bilerek.</b> Ortak bir sınıfa
/// çıkarılsaydı derleyici iki tarafı senkron tutardı ve "banka imza şemasını
/// değiştirdi" hatası imkânsız görünürdü — oysa entegrasyonlarda en sık kırılan şey
/// tam olarak bu (decisions.md madde 35). Gerçeğinde bu tarafı banka yazıyor,
/// doğrulayıcıyı biz onların dokümanından yazıyoruz.
///
/// <c>topup-webhook</c>'un imzasıyla da paylaşılmıyor: orada başlık adını BİZ
/// belirledik (sağlayıcılara verdiğimiz sözleşme), burada bankanın seçtiği başlığı
/// taklit ediyoruz. Algoritmanın aynı olması tesadüf değil — ikisi de yaygın
/// kalıbı kullanıyor — ama aynı sözleşme değiller.
/// </summary>
internal static class BankCallbackSignature
{
    /// <summary>Bankanın kendi başlık adı. Bizim <c>X-Hive-Signature</c>'ımızla ilgisi yok.</summary>
    public const string HeaderName = "X-Bank-Signature";

    private const string Prefix = "sha256=";

    public static string Compute(ReadOnlySpan<byte> body, string secret)
    {
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body, hash);

        return Prefix + Convert.ToHexStringLower(hash);
    }
}
