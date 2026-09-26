using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Responses;

public sealed record AccountResponse(Guid AccountId, AccountType Type, DateTimeOffset CreatedAt)
{
    public static AccountResponse From(OpenAccountResult result)
    {
        return new AccountResponse(result.AccountId, result.Type, result.CreatedAt);
    }

    public static AccountResponse From(AccountSummary summary)
    {
        return new AccountResponse(summary.AccountId, summary.Type, summary.CreatedAt);
    }
}

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record AccountsResponse(IReadOnlyList<AccountResponse> Items, int Size, Guid? NextCursor)
{
    public static AccountsResponse From(AccountPage page)
    {
        return new AccountsResponse([.. page.Items.Select(AccountResponse.From)], page.Size, page.NextCursor);
    }
}
