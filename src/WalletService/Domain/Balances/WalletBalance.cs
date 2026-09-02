using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Balances;

/// <summary>
/// Ledger'dan türetilmiş projeksiyon. Source of truth <c>ledger_entries</c>;
/// bu tablo her zaman yeniden inşa edilebilir, tersi geçerli değil
/// (docs/ledger-schema.md "wallet_balances").
///
/// Optimistic lock buradadır — projede concurrency token taşıyan TEK entity
/// (decisions.md §2).
/// </summary>
public sealed class WalletBalance
{
    private WalletBalance()
    {
        // EF Core materialization.
    }

    private WalletBalance(Guid accountId, Currency currency, DateTimeOffset updatedAt)
    {
        AccountId = accountId;
        Balance = 0m;
        Currency = currency;
        Version = 0;
        UpdatedAt = updatedAt;
    }

    public Guid AccountId { get; private set; }

    public decimal Balance { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>EF Core'da <c>IsConcurrencyToken()</c>. Elle artırılır, DB üretmez.</summary>
    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Money Money => new(Balance, Currency);

    public static WalletBalance OpenFor(Guid accountId, Currency currency, DateTimeOffset createdAt)
    {
        return new WalletBalance(accountId, currency, createdAt);
    }

    /// <summary>
    /// Ledger'a yazılmış bir bacağı projeksiyona uygular. Bakiye ledger'a yazmadan
    /// güncellenmez (CLAUDE.md) — çağıran sırayı bozmaz.
    /// </summary>
    /// <param name="canGoNegative">
    /// <see cref="Domain.Accounts.Account.CanGoNegative"/>. Sistem hesapları negatife düşebilir,
    /// cüzdanlar düşemez; CHECK constraint değil uygulama kuralı (decisions.md §6).
    /// </param>
    public void Apply(Money delta, bool canGoNegative, DateTimeOffset now)
    {
        if (delta.Currency != Currency)
        {
            throw new InvalidOperationException(
                $"Hesap {AccountId} {Currency} tutuyor, {delta.Currency} uygulanamaz.");
        }

        var next = Balance + delta.Amount;

        if (next < 0m && !canGoNegative)
        {
            throw new InsufficientFundsException(AccountId, Money, delta.Abs);
        }

        Balance = next;
        Version++;
        UpdatedAt = now;
    }
}
