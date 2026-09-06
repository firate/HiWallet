using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Responses;

public sealed record AccountDetailResponse(
    Guid AccountId,
    AccountType Type,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AccountWalletResponse> Wallets)
{
    public static AccountDetailResponse From(AccountView view)
    {
        return new AccountDetailResponse(
            view.AccountId,
            view.Type,
            view.CreatedAt,
            view.Wallets
                .Select(w => new AccountWalletResponse(w.WalletId, w.Name, w.Currency, w.Balance))
                .ToArray());
    }
}

public sealed record AccountWalletResponse(Guid WalletId, string Name, string Currency, decimal Balance);
