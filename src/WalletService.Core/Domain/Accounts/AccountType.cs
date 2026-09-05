namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Hesap sahibinin kim olduğu. Ledger çekirdeği buna bakmaz; transfer tipini
/// (p2p/p2b/b2p/b2b) belirleyen policy katmanı bakar (decisions.md madde 6).
/// </summary>
public enum AccountType
{
    Person = 1,
    Business = 2
}
