namespace HiWallet.EdgeApi.Contracts;

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
    Guid AccountId,
    Guid WalletId,
    string State,
    decimal Amount,
    string Currency,
    string DestinationIban,
    decimal? TotalDebited,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record WithdrawalsResponse(IReadOnlyList<WithdrawalResponse> Items, int Size, Guid? NextCursor);

/// <param name="Reason">İptalin sebebi. Müşteriye çekimin durumunda gösteriliyor.</param>
public sealed record CancelWithdrawalRequest(string Reason);
