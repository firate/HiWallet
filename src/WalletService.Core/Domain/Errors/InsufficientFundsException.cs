using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Yetersiz bakiye. İş kuralı reddi → <c>422</c>, hata değil.
/// Concurrency çakışmasıyla (<c>409</c>) KARIŞTIRILMAZ (CLAUDE.md "API").
/// </summary>
public sealed class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(Guid ledgerAccountId, Money available, Money requested)
        : base($"Hesap {ledgerAccountId} için bakiye yetersiz: {available} var, {requested} isteniyor.")
    {
        LedgerAccountId = ledgerAccountId;
        Available = available;
        Requested = requested;
    }

    public Guid LedgerAccountId { get; }

    public Money Available { get; }

    public Money Requested { get; }
}
