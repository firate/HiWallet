namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// Append-only ledger satırı. Yazıldıktan sonra UPDATE/DELETE YOK — düzeltme ters kayıtla
/// (CLAUDE.md). DB tarafında ayrıca <c>REVOKE UPDATE, DELETE</c> ile zorlanır.
/// </summary>
public sealed class LedgerEntry
{
    private LedgerEntry()
    {
        // EF Core materialization.
    }

    internal LedgerEntry(
        Guid transactionId, Guid ledgerAccountId, Money amount, FundType fundType, DateTimeOffset createdAt)
    {
        if (amount.IsZero)
        {
            throw new ArgumentException("Sıfır tutarlı entry yazılmaz — hiçbir şey ifade etmiyor.", nameof(amount));
        }

        TransactionId = transactionId;
        LedgerAccountId = ledgerAccountId;
        Amount = amount.Amount;
        Currency = amount.Currency;
        FundType = fundType;
        CreatedAt = createdAt;
    }

    /// <summary><c>bigserial</c>. DB üretir.</summary>
    public long Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid LedgerAccountId { get; private set; }

    /// <summary>İşaret yönü taşır: credit <c>+</c>, debit <c>-</c>. Ayrı direction kolonu YOK.</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>
    /// Paranın kaynağı (decisions.md madde 36). Sistem hesaplarının bacakları da
    /// taşıyor: bir top-up'ın clearing bacağı cüzdan bacağıyla aynı tipte, yoksa
    /// "clearing'de ne kadar kart parası duruyor" sorusu cevapsız kalırdı.
    ///
    /// Bacakta durduğu için ters kayıt kendiliğinden doğru kovaya dönüyor — iade
    /// orijinalin bacakları okunup negatiflenerek üretiliyor.
    /// </summary>
    public FundType FundType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Money Money => new(Amount, Currency);
}
