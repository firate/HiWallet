using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// İşyeri hesabı açar. Hesap para tutmaz — cüzdanlar tutar (decisions.md madde 20).
/// Bireysel hesap buradan açılmıyor: onu kayıt açıyor (<see cref="OpenPersonAccountCommand"/>).
///
/// <c>Idempotency-Key</c> YOK, transfer ve çekimin aksine. O ikisinde anahtar para
/// hareketini koruyor; burada tekrar eden request yalnızca boş bir hesap daha açıyor.
/// Bedeli bir satır, karşılığı <c>accounts</c> üzerinde ikinci bir unique index.
/// </summary>
/// <param name="Subject">
/// Hesabı açan kimlik. Hesabın ilk kullanıcısı oluyor; hesap ile üyelik aynı
/// transaction'da yazılıyor, kullanıcısız hesap kalmıyor.
/// </param>
public sealed record OpenAccountCommand(AccountType Type, string Subject);

public sealed record OpenAccountResult(Guid AccountId, AccountType Type, DateTimeOffset CreatedAt);
