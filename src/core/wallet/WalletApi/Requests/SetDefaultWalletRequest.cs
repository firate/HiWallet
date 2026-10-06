namespace HiWallet.WalletApi.Requests;

/// <param name="WalletId">Hesabın bu para birimindeki cüzdanlarından biri.</param>
public sealed record SetDefaultWalletRequest(Guid WalletId);
