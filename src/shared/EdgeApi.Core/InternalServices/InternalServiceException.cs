using System.Net;

namespace HiWallet.EdgeApi.InternalServices;

/// <summary>
/// İç servis başarısız bir cevap döndü. Durum kodu ve gövde olduğu gibi taşınıyor;
/// <see cref="Errors.InternalServiceExceptionHandler"/> onları istemciye aynen yazıyor.
/// </summary>
public sealed class InternalServiceException(HttpStatusCode statusCode, string? contentType, byte[] body)
    : Exception($"İç servis {(int)statusCode} döndü.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? ContentType { get; } = contentType;

    public byte[] Body { get; } = body;
}
