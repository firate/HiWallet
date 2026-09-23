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
                .Select(w => new AccountWalletResponse(
                    w.WalletId, w.Name, w.Currency, w.Balance, w.Withdrawable,
                    [.. w.Balances.Select(b => new WalletBalanceResponse(b.FundType, b.Balance))]))
                .ToArray());
    }
}

/// <summary>
/// Liste görünümü de kırılımı taşıyor: hesap ekranında "neden çekemiyorum"
/// sorusunun cevabı tek cüzdana girmeden görünsün (decisions.md madde 36).
/// </summary>
public sealed record AccountWalletResponse(
    Guid WalletId,
    string Name,
    string Currency,
    decimal Balance,
    decimal Withdrawable,
    IReadOnlyList<WalletBalanceResponse> Balances);
