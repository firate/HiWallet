using HiWallet.WalletService.Application.Accounts;

namespace HiWallet.WalletApi.Requests;

/// <param name="Name">
/// Cüzdan adı ("Birikim"). Zorunlu: aynı hesabın aynı para birimindeki cüzdanları
/// başka türlü ayırt edilemiyor (decisions.md madde 20).
/// </param>
public sealed record OpenWalletRequest(string Name, string Currency)
{
    /// <summary>Hesap kimliği rotadan gelir, gövdeden değil — cüzdan hesabın altında.</summary>
    public OpenWalletCommand ToCommand(Guid accountId) => new(accountId, Name, Currency);
}
