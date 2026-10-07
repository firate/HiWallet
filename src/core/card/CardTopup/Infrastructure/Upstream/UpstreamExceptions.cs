using System.Net;

namespace HiWallet.CardTopup.Infrastructure.Upstream;

/// <summary>
/// Çağrılan servis (wallet-api ya da kart sağlayıcısı) cevap vermedi: bağlantı yok, zaman
/// aşımı, açık devre ya da 5xx. Geçici; yüklemenin durumu DEĞİŞMİYOR, istemciye <c>503</c>.
/// </summary>
public sealed class UpstreamUnavailableException(string service, Exception? innerException)
    : Exception($"{service} cevap vermedi.", innerException);

/// <summary>
/// wallet-api isteği reddetti. Durum kodu ve gövde olduğu gibi taşınıyor ve istemciye AYNEN
/// dönüyor: limitin, cüzdanın ve hesabın kararı wallet'ta, bu servis onu yeniden yorumlamıyor.
/// </summary>
public sealed class WalletRejectedException(HttpStatusCode statusCode, string? contentType, byte[] body)
    : Exception($"wallet-api {(int)statusCode} döndü.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? ContentType { get; } = contentType;

    public byte[] Body { get; } = body;
}

/// <summary>
/// Kart sağlayıcısı isteğimizi reddetti (4xx). Yeniden denemek aynı sonucu verir; bizim
/// isteğimiz bozuk.
/// </summary>
public sealed class ProviderRejectedException(HttpStatusCode statusCode, string body)
    : Exception($"Kart sağlayıcısı isteği reddetti ({(int)statusCode}): {body}");
