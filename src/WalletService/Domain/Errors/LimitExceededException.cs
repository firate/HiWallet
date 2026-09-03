using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Limit aşımı. Transfer'den ÖNCE, aynı transaction'ın parçası olarak yakalanır;
/// hiç para hareketi olmaz (overview.md madde 4). İş kuralı reddi → <c>422</c>.
/// </summary>
public sealed class LimitExceededException : DomainException
{
    public LimitExceededException(Guid ownerId, string limitName, Money limit, Money attempted)
        : base($"Sahip {ownerId} için '{limitName}' limiti aşıldı: limit {limit}, denenen {attempted}.")
    {
        OwnerId = ownerId;
        LimitName = limitName;
        Limit = limit;
        Attempted = attempted;
    }

    /// <summary>
    /// Cüzdan değil SAHİP. Limitler sahip bazında uygulanıyor (decisions.md madde 20);
    /// <see cref="InsufficientFundsException.AccountId"/> ise gerçekten cüzdan bazında.
    /// </summary>
    public Guid OwnerId { get; }

    public string LimitName { get; }

    public Money Limit { get; }

    public Money Attempted { get; }
}
