namespace HiWallet.PersonalMobileApi.Responses;

/// <summary>
/// Verilen söz "para gönderildi" değil, "istek kalıcı olarak alındı". Sonuç
/// <c>Location</c>'daki adresten izleniyor.
/// </summary>
/// <param name="Replayed">Aynı anahtarla daha önce başlatılmış; yeni çekim açılmadı.</param>
public sealed record WithdrawalAcceptedResponse(Guid WithdrawalId, string State, bool Replayed);

/// <param name="DestinationIban">Maskeli.</param>
/// <param name="TotalDebited">
/// Cüzdandan çıkan toplam (tutar + komisyon). Düşme yapılana kadar <c>null</c>.
/// </param>
public sealed record WithdrawalResponse(
    Guid WithdrawalId,
    string State,
    decimal Amount,
    string Currency,
    string DestinationIban,
    decimal? TotalDebited,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
