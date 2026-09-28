using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Kaydı tamamlanan kimliğin bireysel hesabı ve ilk TRY cüzdanı. Onboarding açıyor,
/// müşteri değil: açabilseydi kayıt ve doğrulama adımlarını atlardı.
///
/// Kimlik başına tek bireysel hesap; komut tekrar edilebilir. Tekrarında ya da eşzamanlı
/// ikinci açılışta mevcut hesap dönüyor.
/// </summary>
/// <param name="Holder">Kaydı tamamlanan kimlik (<c>sub</c>). Hesabın sahibi ve ilk kullanıcısı.</param>
public sealed record OpenPersonAccountCommand(string Holder);

/// <param name="WalletId">Açılışla birlikte açılan ilk cüzdan.</param>
/// <param name="Replayed">Hesap bu komuttan önce açılmıştı.</param>
public sealed record OpenPersonAccountResult(
    Guid AccountId,
    KycLevel KycLevel,
    Guid WalletId,
    DateTimeOffset CreatedAt,
    bool Replayed);
