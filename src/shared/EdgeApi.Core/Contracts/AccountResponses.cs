namespace HiWallet.EdgeApi.Contracts;

/// <param name="AccountNumber">İnsanın kullandığı on haneli numara; hesaba gelen para bu numarayla.</param>
/// <param name="Type"><c>Person</c> ya da <c>Business</c>.</param>
/// <param name="KycLevel">
/// Bireysel hesabın doğrulama seviyesi (<c>Unknown</c>, <c>Unverified</c>, <c>Verified</c>,
/// <c>Contracted</c>); işyeri hesabında <c>null</c>. Uygulama müşterinin sonraki adımını
/// buna göre gösteriyor.
/// </param>
public sealed record AccountResponse(
    Guid AccountId, string AccountNumber, string Type, string? KycLevel, DateTimeOffset CreatedAt);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record AccountsResponse(IReadOnlyList<AccountResponse> Items, int Size, Guid? NextCursor);

/// <param name="AccountNumber">İnsanın kullandığı on haneli numara; hesaba gelen para bu numarayla.</param>
/// <param name="KycLevel">Bireysel hesabın doğrulama seviyesi; işyeri hesabında <c>null</c>.</param>
/// <param name="AcceptsPromo">Platform fonlu promo bu işyerinde geçiyor mu. Bireysel hesapta hep <c>false</c>.</param>
public sealed record AccountDetailResponse(
    Guid AccountId,
    string AccountNumber,
    string Type,
    string? KycLevel,
    bool AcceptsPromo,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AccountWalletResponse> Wallets);

/// <param name="Withdrawable">IBAN'a çıkabilen kısım; toplamdan ayrı dönüyor.</param>
/// <param name="IsDefault">Para biriminin varsayılan cüzdanı: hesap numarasına gelen para buraya.</param>
public sealed record AccountWalletResponse(
    Guid WalletId,
    string Name,
    string Currency,
    decimal Balance,
    decimal Withdrawable,
    IReadOnlyList<WalletBalanceResponse> Balances,
    bool IsDefault);

/// <param name="WalletId">Hesabın bu para birimindeki cüzdanlarından biri.</param>
public sealed record SetDefaultWalletRequest(Guid WalletId);
