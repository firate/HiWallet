using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Requests;

/// <param name="Type">
/// <c>Person</c> veya <c>Business</c>. Transfer tipini (p2p/p2b/…) bu belirliyor;
/// cüzdana kopyalanmıyor, tek yerde durur (decisions.md madde 20).
/// </param>
public sealed record OpenAccountRequest(AccountType Type)
{
    public OpenAccountCommand ToCommand() => new(Type);
}
