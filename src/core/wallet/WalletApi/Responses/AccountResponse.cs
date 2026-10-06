using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Responses;

/// <param name="AccountNumber">İnsanın kullandığı on haneli numara; hesaba gelen para bu numarayla.</param>
/// <param name="KycLevel">Bireysel hesabın doğrulama seviyesi; işyeri hesabında <c>null</c>.</param>
public sealed record AccountResponse(
    Guid AccountId, string AccountNumber, AccountType Type, KycLevel? KycLevel, DateTimeOffset CreatedAt)
{
    public static AccountResponse From(OpenAccountResult result)
    {
        return new AccountResponse(result.AccountId, result.Number.Value, result.Type, KycLevel: null, result.CreatedAt);
    }

    public static AccountResponse From(AccountSummary summary)
    {
        return new AccountResponse(
            summary.AccountId, summary.Number.Value, summary.Type, summary.KycLevel, summary.CreatedAt);
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
