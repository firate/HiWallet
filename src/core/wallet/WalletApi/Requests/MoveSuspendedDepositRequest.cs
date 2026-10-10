namespace HiWallet.WalletApi.Requests;

/// <param name="AccountNumber">Paranın aktarılacağı hesap; para varsayılan cüzdanına düşüyor.</param>
public sealed record MoveSuspendedDepositRequest(string AccountNumber);
