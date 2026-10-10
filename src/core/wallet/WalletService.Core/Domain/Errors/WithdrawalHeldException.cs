namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Hesabın çekimi bekletmede: telefon numarası yakın zamanda değişti. Reddin kuralı
/// <c>withdrawal_hold</c>; müşteriye bekletmenin sonu gösteriliyor.
/// </summary>
public sealed class WithdrawalHeldException(Guid accountId, DateTimeOffset until)
    : DomainException($"Hesap {accountId} için çekim {until:O} anına kadar kapalı.")
{
    public DateTimeOffset Until { get; } = until;
}
