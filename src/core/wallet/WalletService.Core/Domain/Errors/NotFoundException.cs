using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Var olmayan bir kayda atıf. <see cref="DomainException"/> DEĞİL: o "request geçerliydi,
/// kural izin vermedi" demek ve <c>422</c>'ye eşleniyor. Burada kural işlemedi bile,
/// request geçersiz — <c>404</c> (CLAUDE.md "API").
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

/// <summary>
/// Bu numarada hesap yok. Mesajda numara var: çağıranın kendi yazdığı, kimseye ait olmayan
/// bir değer.
/// </summary>
public sealed class AccountNumberNotFoundException(AccountNumber number)
    : NotFoundException($"Hesap bulunamadı: {number}")
{
    public AccountNumber Number { get; } = number;
}

/// <summary>Promo kampanyası bulunamadı.</summary>
public sealed class PromoCampaignNotFoundException(Guid campaignId)
    : NotFoundException($"Kampanya bulunamadı: {campaignId}")
{
    public Guid CampaignId { get; } = campaignId;
}

/// <summary>Askıdaki havale bulunamadı.</summary>
public sealed class SuspendedDepositNotFoundException(Guid suspendedDepositId)
    : NotFoundException($"Askıdaki havale bulunamadı: {suspendedDepositId}")
{
    public Guid SuspendedDepositId { get; } = suspendedDepositId;
}
