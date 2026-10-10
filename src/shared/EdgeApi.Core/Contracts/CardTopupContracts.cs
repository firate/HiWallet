namespace HiWallet.EdgeApi.Contracts;

/// <summary>
/// Tarayıcının kartla yükleme isteği. Dönüş adresi YOK: BFF onu kendi adresinden kuruyor,
/// tarayıcının verdiği bir adrese ödeme sayfası müşteriyi yollamıyor.
/// </summary>
public sealed record StartCardTopupRequest(Guid WalletId, decimal Amount, string Currency);

/// <summary>Mobil uygulamanın kartla yükleme isteği.</summary>
/// <param name="ReturnUrl">Ödeme sayfasından sonra uygulamaya dönülen adres.</param>
public sealed record StartAppCardTopupRequest(Guid WalletId, decimal Amount, string Currency, string ReturnUrl);

/// <summary>
/// Verilen söz "para yüklendi" değil: müşteri <see cref="PaymentUrl"/>'e gidip kartını
/// giriyor, sonuç <c>Location</c>'daki adresten izleniyor.
/// </summary>
/// <param name="Replayed">Aynı anahtarla daha önce başlatılmış; yeni yükleme açılmadı.</param>
public sealed record CardTopupAcceptedResponse(
    Guid CardTopupId,
    Guid WalletId,
    string State,
    decimal Amount,
    string Currency,
    string? PaymentUrl,
    DateTimeOffset ExpiresAt,
    string? FailureReason,
    bool Replayed);

/// <param name="State"><c>created</c>, <c>pending</c>, <c>paid</c>, <c>failed</c>, <c>rejected</c>.</param>
public sealed record CardTopupResponse(
    Guid CardTopupId,
    Guid WalletId,
    string State,
    decimal Amount,
    string Currency,
    string? PaymentUrl,
    DateTimeOffset ExpiresAt,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record CardTopupsResponse(IReadOnlyList<CardTopupResponse> Items, int Size, Guid? NextCursor);
