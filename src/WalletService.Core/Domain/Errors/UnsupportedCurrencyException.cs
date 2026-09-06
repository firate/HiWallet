using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// İstenen para biriminde sistem hesapları (clearing, revenue) açılmamış.
///
/// Girdi kuralı DEĞİL — kod ISO 4217'ye uygun, istek biçimsel olarak kusursuz.
/// Sınırdaki validator bunu bilemez: cevap ledger'da, hangi sistem hesaplarının
/// seed edildiğinde. Bu yüzden <see cref="DomainException"/> ve <c>422</c>.
/// </summary>
public sealed class UnsupportedCurrencyException(Currency currency, Currency supported)
    : DomainException(
        $"{currency} için sistem hesapları açılmamış; bu para biriminde cüzdan " +
        $"kullanılamaz. Desteklenen: {supported}.")
{
    public Currency Currency { get; } = currency;
}
