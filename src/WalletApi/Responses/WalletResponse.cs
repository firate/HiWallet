using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Application.Balances;

namespace HiWallet.WalletApi.Responses;

/// <param name="Balance">
/// <c>ledger_balances</c> projeksiyonundan. Açılışta her zaman sıfır — cüzdana para
/// yalnızca ledger üzerinden girer (top-up ya da transfer), doğrudan yazılamaz.
/// </param>
public sealed record WalletResponse(
    Guid WalletId,
    Guid AccountId,
    string Name,
    string Currency,
    decimal Balance)
{
    public static WalletResponse From(OpenWalletResult result)
    {
        return new WalletResponse(
            result.WalletId, result.AccountId, result.Name, result.Currency, result.Balance);
    }

    public static WalletResponse From(WalletView view)
    {
        return new WalletResponse(
            view.WalletId, view.AccountId, view.Name, view.Currency, view.Balance);
    }
}
