using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Responses;

public sealed record AccountResponse(Guid AccountId, AccountType Type, DateTimeOffset CreatedAt)
{
    public static AccountResponse From(OpenAccountResult result)
    {
        return new AccountResponse(result.AccountId, result.Type, result.CreatedAt);
    }
}
