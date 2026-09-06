using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Müşteri hesabı açar. Hesap para tutmaz — cüzdanlar tutar (decisions.md madde 20).
///
/// <c>Idempotency-Key</c> YOK, transfer ve çekimin aksine. O ikisinde anahtar para
/// hareketini koruyor; burada tekrar eden istek yalnızca boş bir hesap daha açıyor.
/// Bedeli bir satır, karşılığı <c>accounts</c> üzerinde ikinci bir unique index.
/// </summary>
public sealed record OpenAccountCommand(AccountType Type);

public sealed record OpenAccountResult(Guid AccountId, AccountType Type, DateTimeOffset CreatedAt);
