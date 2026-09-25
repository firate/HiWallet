namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Promo yükleme kuralı reddi → <c>422</c> (decisions.md madde 37): fonlayan işyeri
/// değil, ya da işyeri kendi hesabına promo vermeye çalışıyor.
/// </summary>
public sealed class PromoGrantRejectedException(string message) : DomainException(message);
