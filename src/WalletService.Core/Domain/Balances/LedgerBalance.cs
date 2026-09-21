using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Balances;

/// <summary>
/// Ledger'dan türetilmiş projeksiyon. Source of truth <c>ledger_entries</c>;
/// bu tablo her zaman yeniden inşa edilebilir, tersi geçerli değil
/// (docs/ledger-schema.md "ledger_balances").
///
/// Optimistic lock buradadır — wallet sınırında concurrency token taşıyan TEK entity
/// (decisions.md madde 2).
///
/// Satırın anahtarı <c>(ledger_account_id, fund_type)</c>: bakiye paranın kaynağına
/// göre bölünüyor (decisions.md madde 36). Aynı cüzdanın iki kovası birbirini
/// bloklamıyor, kilit kova bazında.
/// </summary>
public sealed class LedgerBalance
{
    private LedgerBalance()
    {
        // EF Core materialization.
    }

    private LedgerBalance(Guid ledgerAccountId, Currency currency, FundType fundType, DateTimeOffset updatedAt)
    {
        LedgerAccountId = ledgerAccountId;
        Balance = 0m;
        Currency = currency;
        FundType = fundType;
        Version = 0;
        UpdatedAt = updatedAt;
    }

    public Guid LedgerAccountId { get; private set; }

    public decimal Balance { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>Bu satırın hangi kovayı tuttuğu (decisions.md madde 36).</summary>
    public FundType FundType { get; private set; }

    /// <summary>EF Core'da <c>IsConcurrencyToken()</c>. Elle artırılır, DB üretmez.</summary>
    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Money Money => new(Balance, Currency);

    public static LedgerBalance OpenFor(
        Guid ledgerAccountId, Currency currency, FundType fundType, DateTimeOffset createdAt)
    {
        return new LedgerBalance(ledgerAccountId, currency, fundType, createdAt);
    }

    /// <summary>
    /// Ledger'a yazılmış bir bacağı projeksiyona uygular. Bakiye ledger'a yazmadan
    /// güncellenmez (CLAUDE.md) — çağıran sırayı bozmaz.
    /// </summary>
    /// <param name="canGoNegative">
    /// <see cref="Domain.Accounts.Account.CanGoNegative"/>. Sistem hesapları negatife düşebilir,
    /// cüzdanlar düşemez; CHECK constraint değil uygulama kuralı (decisions.md madde 6).
    /// </param>
    public void Apply(Money delta, bool canGoNegative, DateTimeOffset now)
    {
        if (delta.Currency != Currency)
        {
            throw new InvalidOperationException(
                $"Hesap {LedgerAccountId} {Currency} tutuyor, {delta.Currency} uygulanamaz.");
        }

        var next = Balance + delta.Amount;

        if (next < 0m && !canGoNegative)
        {
            throw new InsufficientFundsException(LedgerAccountId, Money, delta.Abs);
        }

        Balance = next;
        Version++;
        UpdatedAt = now;
    }
}
