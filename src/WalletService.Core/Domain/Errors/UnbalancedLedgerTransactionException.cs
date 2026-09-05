namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Zero-sum invariant ihlali. Bu bir iş kuralı reddi DEĞİL — koda giren bir hatadır,
/// client'a 422 dönülmez, 500 olarak loglanır. <see cref="DomainException"/>'dan
/// bilinçli olarak türemez.
/// </summary>
public sealed class UnbalancedLedgerTransactionException : Exception
{
    public UnbalancedLedgerTransactionException(Guid transactionId, string detail)
        : base($"Ledger transaction {transactionId} dengeli değil. {detail}")
    {
        TransactionId = transactionId;
    }

    public Guid TransactionId { get; }
}
