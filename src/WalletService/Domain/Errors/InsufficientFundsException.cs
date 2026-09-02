using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Yetersiz bakiye. İş kuralı reddi → <c>422</c>, hata değil.
/// Concurrency çakışmasıyla (<c>409</c>) KARIŞTIRILMAZ (CLAUDE.md "API").
/// </summary>
public sealed class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(Guid accountId, Money available, Money requested)
        : base($"Hesap {accountId} için bakiye yetersiz: {available} var, {requested} isteniyor.")
    {
        AccountId = accountId;
        Available = available;
        Requested = requested;
    }

    public Guid AccountId { get; }

    public Money Available { get; }

    public Money Requested { get; }
}
