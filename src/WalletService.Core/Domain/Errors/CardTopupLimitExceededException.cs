using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Kartla yükleme seviyenin limitine sığmıyor; ödeme açılmıyor ve kart çekilmiyor. Hesaba
/// gelen paranın limitiyle aynı kontrol, ama burada parayı alan da isteyen de müşterinin
/// kendisi: mesaj ona söylüyor. İş kuralı reddi → <c>422</c>.
/// </summary>
public sealed class CardTopupLimitExceededException(string limitName, Money limit)
    : DomainException($"Bu yükleme doğrulama seviyenin limitine sığmıyor: '{limitName}' limiti {limit}.")
{
    public string LimitName { get; } = limitName;

    public Money Limit { get; } = limit;
}
