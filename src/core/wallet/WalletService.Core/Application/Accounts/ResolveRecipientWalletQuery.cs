using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Hesap numarasına gönderilen paranın düşeceği cüzdan: alıcının bu para birimindeki
/// varsayılan cüzdanı. Transfer çekirdeği cüzdandan cüzdana çalışıyor; numara sınırda
/// cüzdana çevriliyor.
/// </summary>
/// <returns>Cüzdanın kimliği.</returns>
public sealed record ResolveRecipientWalletQuery(AccountNumber Number, string Currency);
