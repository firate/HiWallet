using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>Numaranın hesabı. Kimin görebileceğine uç karar veriyor.</summary>
/// <returns>Hesabın kimliği.</returns>
public sealed record FindAccountByNumberQuery(AccountNumber Number);
