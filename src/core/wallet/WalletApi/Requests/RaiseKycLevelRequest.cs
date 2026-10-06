using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Requests;

/// <param name="Level">Ulaşılan seviye. Hesap zaten o seviyede ya da üstündeyse değişmiyor.</param>
public sealed record RaiseKycLevelRequest(KycLevel Level)
{
    public RaiseKycLevelCommand ToCommand(Guid accountId) => new(accountId, Level);
}
