namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Hesabın bu para birimindeki varsayılan cüzdanını değiştirir. Müşterinin tercihi;
/// cüzdan hesabın kendi cüzdanı ve aynı para biriminden olmalı.
/// </summary>
public sealed record SetDefaultWalletCommand(Guid AccountId, string Currency, Guid WalletId);
