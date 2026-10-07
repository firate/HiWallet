namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// İşyeri hesabına müşteri doğrulama seviyesi verilemez; işyerinin doğrulaması ayrı bir
/// iş. İş kuralı reddi → <c>422</c>.
/// </summary>
public sealed class KycLevelNotApplicableException(Guid accountId)
    : DomainException($"Hesap {accountId} bireysel değil; doğrulama seviyesi yalnızca bireysel hesapta var.")
{
    public Guid AccountId { get; } = accountId;
}
