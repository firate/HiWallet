using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Alıcının seviyesinin gelen para limiti aşılıyor. Reddi gönderen görüyor; mesaj alıcının
/// hesabını söylemiyor, gönderen yalnızca cüzdanı biliyor. İş kuralı reddi → <c>422</c>.
/// </summary>
public sealed class IncomingLimitExceededException(string limitName, Money limit)
    : DomainException($"Alıcı bu tutarı alamıyor: '{limitName}' limiti {limit}.")
{
    public string LimitName { get; } = limitName;

    public Money Limit { get; } = limit;
}
