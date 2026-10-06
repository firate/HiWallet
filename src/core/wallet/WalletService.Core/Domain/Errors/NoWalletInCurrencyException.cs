using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Hesap numarasına gönderilen paranın düşeceği cüzdan yok: alıcının o para biriminde
/// cüzdanı yok. Hesabında kendiliğinden cüzdan AÇILMIYOR; müşterinin haberi olmadan
/// hesabında yeni bir şey açılmış olurdu. Mesaj alıcının hesabı hakkında başka bir şey
/// söylemiyor.
/// </summary>
public sealed class NoWalletInCurrencyException(Currency currency)
    : DomainException($"Alıcı {currency} ile ödeme alamıyor.")
{
    public Currency Currency { get; } = currency;
}
