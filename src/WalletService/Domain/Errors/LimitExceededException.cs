using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Limit aşımı. Transfer'den ÖNCE, aynı transaction'ın parçası olarak yakalanır;
/// hiç para hareketi olmaz (Bölüm 3 madde 4). İş kuralı reddi → <c>422</c>.
/// </summary>
public sealed class LimitExceededException : DomainException
{
    public LimitExceededException(Guid accountId, string limitName, Money limit, Money attempted)
        : base($"Hesap {accountId} için '{limitName}' limiti aşıldı: limit {limit}, denenen {attempted}.")
    {
        AccountId = accountId;
        LimitName = limitName;
        Limit = limit;
        Attempted = attempted;
    }

    public Guid AccountId { get; }

    public string LimitName { get; }

    public Money Limit { get; }

    public Money Attempted { get; }
}
