using HiWallet.WalletService.Domain.Errors;

namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// Bir atomik para hareketinin başlığı. Entry'lerinin toplamı sıfır olmak zorunda
/// (CLAUDE.md "Ledger"). Şema: docs/ledger-schema.md "ledger_transactions".
/// </summary>
public sealed class LedgerTransaction
{
    private readonly List<LedgerEntry> _entries = [];

    private LedgerTransaction()
    {
        // EF Core materialization.
    }

    private LedgerTransaction(
        Guid id,
        LedgerTransactionType type,
        Guid accountId,
        string? idempotencyKey,
        Guid? correlationId,
        DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        AccountId = accountId;
        IdempotencyKey = idempotencyKey;
        CorrelationId = correlationId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public LedgerTransactionType Type { get; private set; }

    /// <summary>
    /// İşlemin idempotency KAPSAMI olan hesap — "isteği başlatan hesap" değil.
    /// İç işlemlerde de dolar; nullable yapılmaz (decisions.md madde 15).
    /// </summary>
    public Guid AccountId { get; private set; }

    public string? IdempotencyKey { get; private set; }

    /// <summary>Saga / webhook event ilişkisi.</summary>
    public Guid? CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public static LedgerTransaction Create(
        Guid id,
        LedgerTransactionType type,
        Guid accountId,
        DateTimeOffset createdAt,
        string? idempotencyKey = null,
        Guid? correlationId = null)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Idempotency kapsamı boş olamaz (decisions.md madde 15).", nameof(accountId));
        }

        return new LedgerTransaction(id, type, accountId, idempotencyKey, correlationId, createdAt);
    }

    /// <summary>
    /// Bacak ekler. İşaret çağıranda: credit <c>+</c>, debit <c>-</c>.
    /// Tutar sıfırsa reddedilir.
    /// </summary>
    public LedgerTransaction AddEntry(Guid accountId, Money amount)
    {
        _entries.Add(new LedgerEntry(Id, accountId, amount, CreatedAt));
        return this;
    }

    /// <summary>
    /// Zero-sum invariant'ı. DB tarafındaki deferred constraint trigger'ın kopyası değil,
    /// ondan önce çalışan ve daha erken hata veren kontrol (decisions.md madde 5).
    /// Trigger currency'ye bakmaz; burası bakar — farklı para birimleri ayrı ayrı sıfırlanmalı.
    /// </summary>
    public void AssertBalanced()
    {
        if (_entries.Count < 2)
        {
            throw new UnbalancedLedgerTransactionException(Id, $"En az iki bacak gerekir, {_entries.Count} var.");
        }

        foreach (var byCurrency in _entries.GroupBy(e => e.Currency))
        {
            var total = byCurrency.Sum(e => e.Amount);

            if (total != 0m)
            {
                throw new UnbalancedLedgerTransactionException(Id, $"{byCurrency.Key} bacaklarının toplamı sıfır değil: {total}.");
            }
        }
    }
}
