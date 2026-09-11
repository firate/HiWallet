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
        Guid ledgerAccountId,
        Actor actor,
        string idempotencyKey,
        Guid? correlationId,
        DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        LedgerAccountId = ledgerAccountId;
        ActorType = actor.Type;
        ActorId = actor.Id;
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
    public Guid LedgerAccountId { get; private set; }

    /// <summary>
    /// İşlemi başlatan taraf (decisions.md madde 34). İkisi de NOT NULL: nullable
    /// olsaydı "müşteri yaptı" ile "kaydedilmedi" ayırt edilemezdi.
    ///
    /// Yaratılışta yazılır, bir daha güncellenmez — ledger append-only.
    /// </summary>
    public ActorType ActorType { get; private set; }

    public string ActorId { get; private set; } = string.Empty;

    /// <summary>
    /// ZORUNLU (decisions.md madde 4). Nullable DEĞİL: anahtarsız bir satır
    /// deduplike edilemez ve tekrarı hiçbir şeye takılmadan ikinci kez yazılır.
    /// Her yazma yolunun bir anahtarı var — transfer'de istemci üretiyor, iç
    /// akışlarda kaynağın kendi kimliğinden türüyor.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Saga / webhook event ilişkisi.</summary>
    public Guid? CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    /// <param name="actor">
    /// ZORUNLU ve varsayılanı YOK (decisions.md madde 34): her yazma yolu kökenini
    /// beyan etmek zorunda. Varsayılan verilseydi yeni bir handler onu sessizce
    /// devralır ve yanlış aktörle kalıcı kayıt yazardı.
    /// </param>
    public static LedgerTransaction Create(
        Guid id,
        LedgerTransactionType type,
        Guid ledgerAccountId,
        Actor actor,
        DateTimeOffset createdAt,
        string idempotencyKey,
        Guid? correlationId = null)
    {
        if (ledgerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Idempotency kapsamı boş olamaz (decisions.md madde 15).", nameof(ledgerAccountId));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key'siz ledger işlemi açılamaz (decisions.md madde 4).", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(actor.Id))
        {
            throw new ArgumentException("Aktör belirtilmeden ledger işlemi açılamaz (decisions.md madde 34).", nameof(actor));
        }

        return new LedgerTransaction(id, type, ledgerAccountId, actor, idempotencyKey, correlationId, createdAt);
    }

    /// <summary>
    /// Bacak ekler. İşaret çağıranda: credit <c>+</c>, debit <c>-</c>.
    /// Tutar sıfırsa reddedilir.
    /// </summary>
    public LedgerTransaction AddEntry(Guid ledgerAccountId, Money amount)
    {
        _entries.Add(new LedgerEntry(Id, ledgerAccountId, amount, CreatedAt));
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
