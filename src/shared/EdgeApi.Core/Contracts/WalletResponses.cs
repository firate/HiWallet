namespace HiWallet.EdgeApi.Contracts;

/// <param name="Withdrawable">IBAN'a çıkabilen kısım; toplamdan ayrı dönüyor.</param>
/// <param name="Balances">Kova kırılımı. Sıfır bakiyeli kovalar da dönüyor.</param>
public sealed record WalletResponse(
    Guid WalletId,
    Guid AccountId,
    string Name,
    string Currency,
    decimal Balance,
    decimal Withdrawable,
    IReadOnlyList<WalletBalanceResponse> Balances);

public sealed record WalletBalanceResponse(string FundType, decimal Balance);

/// <param name="MovementId">Hareketin kimliği ve bir sonraki sayfanın cursor'ı.</param>
/// <param name="Amount">İşaret yön taşır: cüzdana giren <c>+</c>, çıkan <c>-</c>.</param>
public sealed record WalletMovementResponse(
    long MovementId,
    Guid TransactionId,
    string Type,
    decimal Amount,
    string Currency,
    string FundType,
    DateTimeOffset CreatedAt);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record WalletMovementsResponse(
    IReadOnlyList<WalletMovementResponse> Items,
    int Size,
    long? NextCursor);

/// <param name="Remaining">Tutardan harcama ve süre sonu tüketimleri düşülmüş kalan.</param>
/// <param name="Scope"><c>selected_businesses</c> ya da <c>all_businesses</c>.</param>
public sealed record WalletPromoResponse(
    Guid GrantId,
    decimal Amount,
    decimal Remaining,
    string Currency,
    string Funder,
    string Scope,
    IReadOnlyList<Guid> MerchantAccountIds,
    DateTimeOffset? ExpiresAt,
    bool Expired,
    DateTimeOffset CreatedAt);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record WalletPromosResponse(
    IReadOnlyList<WalletPromoResponse> Items,
    int Size,
    Guid? NextCursor);
