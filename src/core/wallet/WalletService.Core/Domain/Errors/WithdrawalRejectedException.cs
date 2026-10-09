namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Çekim iş kuralı gereği reddedildi. <see cref="DomainException"/> olması önemli:
/// akış bunu hata değil CEVAP olarak ele alıyor ve saga'ya bildiriyor.
/// </summary>
public sealed class WithdrawalRejectedException(string message) : DomainException(message);
