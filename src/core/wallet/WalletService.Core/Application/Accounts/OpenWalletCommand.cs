namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Hesaba cüzdan açar. Aynı hesabın aynı para biriminde birden fazla cüzdanı olabilir
/// (decisions.md madde 20) — bu yüzden <paramref name="Name"/> zorunlu, tek ayırt edici o.
/// </summary>
public sealed record OpenWalletCommand(Guid AccountId, string Name, string Currency);

public sealed record OpenWalletResult(
    Guid WalletId,
    Guid AccountId,
    string Name,
    string Currency,
    decimal Balance,
    DateTimeOffset CreatedAt);
