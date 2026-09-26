namespace HiWallet.EdgeApi.Contracts;

/// <param name="Type"><c>Person</c> ya da <c>Business</c>.</param>
public sealed record AccountResponse(Guid AccountId, string Type, DateTimeOffset CreatedAt);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record AccountsResponse(IReadOnlyList<AccountResponse> Items, int Size, Guid? NextCursor);

public sealed record AccountDetailResponse(
    Guid AccountId,
    string Type,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AccountWalletResponse> Wallets);

/// <param name="Withdrawable">IBAN'a çıkabilen kısım; toplamdan ayrı dönüyor.</param>
public sealed record AccountWalletResponse(
    Guid WalletId,
    string Name,
    string Currency,
    decimal Balance,
    decimal Withdrawable,
    IReadOnlyList<WalletBalanceResponse> Balances);
