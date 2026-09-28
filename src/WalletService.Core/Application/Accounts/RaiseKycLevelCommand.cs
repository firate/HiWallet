using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Bireysel hesabın doğrulama seviyesini yükseltir. Hesap zaten o seviyede ya da
/// üstündeyse değişmiyor ve mevcut seviye dönüyor: komut tekrar edilebilir.
/// </summary>
public sealed record RaiseKycLevelCommand(Guid AccountId, KycLevel Level);

/// <param name="Changed">Bu komut seviyeyi değiştirdi.</param>
public sealed record KycLevelResult(Guid AccountId, KycLevel KycLevel, bool Changed);
