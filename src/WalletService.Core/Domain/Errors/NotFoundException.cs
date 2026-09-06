namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Var olmayan bir kayda atıf. <see cref="DomainException"/> DEĞİL: o "istek geçerliydi,
/// kural izin vermedi" demek ve <c>422</c>'ye eşleniyor. Burada kural işlemedi bile,
/// istek geçersiz — <c>404</c> (CLAUDE.md "API").
///
/// Ortak taban olmasının sebebi HTTP eşlemesi: <c>ProblemDetailsSetup</c> tek tip
/// yakalıyor. Yeni bir "bulunamadı" türü eklendiğinde eşleme kendiliğinden çalışsın
/// diye — tip listesi tutmak, listeye eklemeyi unutunca <c>500</c> üretirdi.
/// </summary>
public abstract class NotFoundException(string message) : Exception(message);

/// <summary>Cüzdan bulunamadı.</summary>
public sealed class WalletNotFoundException(Guid walletId)
    : NotFoundException($"Cüzdan bulunamadı: {walletId}")
{
    public Guid WalletId { get; } = walletId;
}

/// <summary>Müşteri hesabı bulunamadı.</summary>
public sealed class AccountNotFoundException(Guid accountId)
    : NotFoundException($"Hesap bulunamadı: {accountId}")
{
    public Guid AccountId { get; } = accountId;
}
