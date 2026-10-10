using HiWallet.WalletService.Application.Accounts;

namespace HiWallet.WalletApi.Requests;

/// <param name="Until">Çekimin kapalı kalacağı son an. Mevcut bekletme daha uzunsa değişmiyor.</param>
public sealed record HoldWithdrawalsRequest(DateTimeOffset Until)
{
    public HoldWithdrawalsCommand ToCommand(Guid accountId) => new(accountId, Until);
}
