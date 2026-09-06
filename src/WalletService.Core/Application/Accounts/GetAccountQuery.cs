using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Hesap ve altındaki cüzdanlar. Cüzdan listesi buraya bağlı: bir hesabın aynı para
/// biriminde birden fazla cüzdanı olabildiği için (decisions.md madde 20) "hesabın
/// cüzdanı" tekil bir şey değil, sorulması gereken bir liste.
/// </summary>
public sealed record GetAccountQuery(Guid AccountId);

public sealed record AccountView(
    Guid AccountId,
    AccountType Type,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AccountWalletView> Wallets);

public sealed record AccountWalletView(
    Guid WalletId,
    string Name,
    string Currency,
    decimal Balance);
