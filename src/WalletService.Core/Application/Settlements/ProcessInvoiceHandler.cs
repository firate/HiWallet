using HiWallet.Shared.Contracts.Settlements;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Settlements;

/// <summary>
/// Dönem sonu faturasını işler (<c>decisions.md</c> madde 11).
///
/// <b>Tutmuyorsa ledger'a HİÇBİR ŞEY yazılmıyor.</b> Sistem hangi tarafın haklı
/// olduğuna karar veremez: fatura tutarını sorgusuz yazmak yanlış gider kaydı
/// üretir, beklenen tutarı yazmak da yanlış çünkü bankadan çıkan para faturadaki
/// tutar. İkisi de sonradan ters kayıt gerektirir. Yazmamak doğru olan — kayıt
/// <c>PendingReview</c>'da bekliyor ve alarm üretiliyor.
///
/// <b>Yalnızca Invoiced model.</b> Net modelde ücret settlement anında kesildi ve
/// fatura diye bir adım yok (madde 10); öyle bir sağlayıcıdan fatura gelmesi ya
/// sağlayıcının modeli değiştirdiği ya konfigürasyonun eskidiği anlamına geliyor.
/// </summary>
public sealed class ProcessInvoiceHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    ProviderPolicy providers,
    IClock clock,
    ILogger<ProcessInvoiceHandler> logger)
{
    /// <summary>
    /// Oransal tolerans: beklenenin binde biri. Kuruş farkı kaçınılmaz — sağlayıcı
    /// işlem başına yuvarlıyor, biz ondalık tutuyoruz ve fark binlerce işlemde
    /// birikiyor (madde 11).
    /// </summary>
    private const decimal ToleranceRate = 0.001m;

    /// <summary>
    /// Taban tolerans. Küçük faturalarda oransal eşik kuruşun altına düşüyor ve
    /// tek bir yuvarlama farkı bile faturayı incelemeye atardı.
    /// </summary>
    private const decimal MinimumTolerance = 1m;

    public async Task<ProcessInvoiceResult> HandleAsync(
        ProviderInvoiceReceived message, CancellationToken ct)
    {
        var currency = Currency.From(message.Currency);
        var terms = providers.For(message.Provider);

        if (terms.FeeSettlement is not FeeSettlement.Invoiced)
        {
            throw new InvoiceRejectedException(
                message.Provider, message.InvoiceRef,
                $"Sağlayıcı {terms.FeeSettlement} modelde; bu modelde ücret settlement " +
                "anında kesiliyor ve fatura adımı yok.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = clock.UtcNow;

        // --- Idempotency kapısı ----------------------------------------------------
        // Aynı faturayı iki kez işlemek doğrudan yanlış gider kaydı ve manuel
        // tetiklenen akışlarda gerçek bir risk (madde 11). Kapı ledger'la AYNI
        // transaction'da: ayrı commit'lerde aradaki çökme ya "işlendi ama gider
        // yazılmadı" ya da "gider iki kez yazıldı" bırakırdı.
        var existing = await db.ProviderInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.Provider == message.Provider && i.InvoiceRef == message.InvoiceRef, ct);

        if (existing is not null)
        {
            await transaction.RollbackAsync(ct);

            logger.LogInformation(
                "Fatura zaten işlenmiş, atlanıyor. {Provider}/{InvoiceRef} → {Status}",
                message.Provider, message.InvoiceRef, existing.Status);

            return new ProcessInvoiceResult(existing.Status, existing.LedgerTransactionId, Replayed: true);
        }

        // --- Kapsam ----------------------------------------------------------------
        var fees = await LoadCoveredFeesAsync(db, message, ct);
        var expected = fees.Sum(f => f.ExpectedAmount);
        var difference = Math.Abs(message.Amount - expected);
        var tolerance = Math.Max(expected * ToleranceRate, MinimumTolerance);

        var invoice = new ProviderInvoice
        {
            Id = Guid.NewGuid(),
            Provider = message.Provider,
            InvoiceRef = message.InvoiceRef,
            Amount = message.Amount,
            ExpectedAmount = expected,
            Currency = currency.Code,
            FeeCount = fees.Count,
            ReceivedAt = now
        };

        // --- Uyuşmazlık --------------------------------------------------------------
        if (difference > tolerance)
        {
            invoice.Status = ProviderInvoiceStatus.PendingReview;
            invoice.Note =
                $"Fark {difference} {currency}, tolerans {tolerance}. " +
                $"Fatura {message.Amount}, beklenen {expected} ({fees.Count} kalem).";

            db.ProviderInvoices.Add(invoice);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // Alarm. Ücret satırlarına DOKUNULMADI: faturalanmamış olarak kalıyorlar
            // ki düzeltilmiş fatura geldiğinde yeniden kapsansınlar.
            logger.LogError(
                "Fatura beklenen toplamla tutmadı, incelemeye alındı. {Provider}/{InvoiceRef}: {Note}",
                message.Provider, message.InvoiceRef, invoice.Note);

            return new ProcessInvoiceResult(
                ProviderInvoiceStatus.PendingReview, LedgerTransactionId: null, Replayed: false);
        }

        // --- Ledger ------------------------------------------------------------------
        // Tolerans içindeki fark loglanıyor ve kayıt YAZILIYOR (madde 11). Yazılan
        // tutar faturadaki: bankadan çıkan para o.
        var expense = await LoadSystemAccountAsync(
            db, LedgerAccountType.ProviderExpense, message.Provider, currency, ct);
        var nostro = await LoadNostroAsync(db, message, currency, ct);

        var transactionId = Guid.NewGuid();

        // Idempotency kapsamı gider hesabı, key ise fatura numarası
        // (ledger-schema.md "Invoiced model").
        var tx = LedgerTransaction.Create(
            transactionId,
            LedgerTransactionType.ProviderInvoice,
            expense.Id,
            SystemActors.ProviderInvoice,
            now,
            IdempotencyKey(message));

        tx.AddEntry(expense.Id, new Money(-message.Amount, currency));

        // nostro POZİTİF: varlık hesabı ve bu ledger'da varlıklar negatif duruyor,
        // yani bankadan para ÇIKINCA sıfıra doğru hareket ediyor.
        tx.AddEntry(nostro.Id, new Money(message.Amount, currency));

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        invoice.Status = ProviderInvoiceStatus.Applied;
        invoice.LedgerTransactionId = transactionId;

        if (difference > 0m)
        {
            invoice.Note = $"Tolerans içinde fark: {difference} {currency}.";

            logger.LogInformation(
                "Fatura tolerans içinde farklı, yazıldı. {Provider}/{InvoiceRef}: {Note}",
                message.Provider, message.InvoiceRef, invoice.Note);
        }

        db.ProviderInvoices.Add(invoice);

        foreach (var fee in fees)
        {
            fee.InvoiceRef = message.InvoiceRef;
            fee.LedgerTransactionId = transactionId;
        }

        await ApplyBalancesAsync(db, tx, now, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Fatura işlendi. {Provider}/{InvoiceRef} → {TransactionId}, {Amount} {Currency}, " +
            "{Count} ücret satırı kapandı.",
            message.Provider, message.InvoiceRef, transactionId,
            message.Amount, currency.Code, fees.Count);

        return new ProcessInvoiceResult(ProviderInvoiceStatus.Applied, transactionId, Replayed: false);
    }

    public static string IdempotencyKey(ProviderInvoiceReceived message) =>
        $"{message.Provider}:invoice:{message.InvoiceRef}";

    /// <summary>
    /// Faturanın kapsadığı ücret satırları: bu sağlayıcının HENÜZ FATURALANMAMIŞ
    /// olanları.
    ///
    /// Referans listesi verilmişse ona göre daraltılıyor; verilmemişse (bazı
    /// sağlayıcılar yalnızca toplam gönderiyor) kapsam faturalanmamışların tamamı.
    /// İkinci durumda madde 11'deki kalem bazlı fark analizi yapılamıyor, yalnızca
    /// toplam karşılaştırılıyor — bu bir eksiklik değil, sağlayıcının verdiği
    /// bilginin sınırı.
    /// </summary>
    private static async Task<List<ProviderFee>> LoadCoveredFeesAsync(
        WalletDbContext db, ProviderInvoiceReceived message, CancellationToken ct)
    {
        var query = db.ProviderFees
            .Where(f => f.Provider == message.Provider && f.InvoiceRef == null);

        if (message.ProviderRefs.Count > 0)
        {
            var refs = message.ProviderRefs.ToArray();
            query = query.Where(f => f.ProviderRef != null && refs.Contains(f.ProviderRef));
        }

        return await query.ToListAsync(ct);
    }

    private static async Task ApplyBalancesAsync(
        WalletDbContext db, LedgerTransaction tx, DateTimeOffset now, CancellationToken ct)
    {
        var deltas = tx.Entries
            .Select(e => (e.LedgerAccountId, e.Money))
            .OrderBy(x => x.LedgerAccountId)
            .ToArray();

        foreach (var (ledgerAccountId, delta) in deltas)
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(b => b.LedgerAccountId == ledgerAccountId, ct)
                          ?? throw new InvalidOperationException($"Bakiye satırı yok: {ledgerAccountId}");

            balance.Apply(delta, canGoNegative: true, now);
        }
    }

    private static async Task<LedgerAccount> LoadNostroAsync(
        WalletDbContext db, ProviderInvoiceReceived message, Currency currency, CancellationToken ct)
    {
        var candidates = await db.LedgerAccounts
            .Where(a => a.Type == LedgerAccountType.Nostro && a.Currency == currency)
            .OrderBy(a => a.Provider)
            .Take(2)
            .ToListAsync(ct);

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvoiceRejectedException(
                message.Provider, message.InvoiceRef, $"{currency} nostro hesabı yok."),
            _ => throw new InvoiceRejectedException(
                message.Provider, message.InvoiceRef,
                $"{currency} için birden fazla nostro hesabı var; faturanın hangisinden " +
                "ödendiği konfigürasyonla belirtilmeli.")
        };
    }

    private static async Task<LedgerAccount> LoadSystemAccountAsync(
        WalletDbContext db,
        LedgerAccountType type,
        string provider,
        Currency currency,
        CancellationToken ct)
    {
        return await db.LedgerAccounts
                   .FirstOrDefaultAsync(
                       a => a.Type == type && a.Provider == provider && a.Currency == currency, ct)
               ?? throw new InvoiceRejectedException(
                   provider, invoiceRef: null, $"'{provider}' sağlayıcısının {currency} {type} hesabı yok.");
    }
}

/// <summary>
/// Fatura kalıcı olarak işlenemez: yanlış model, eksik sistem hesabı. Uyuşmazlıkla
/// KARIŞTIRILMAMALI — o bir hata değil, incelemeye düşen geçerli bir fatura ve
/// kaydı tutuluyor.
/// </summary>
public sealed class InvoiceRejectedException(string provider, string? invoiceRef, string reason)
    : Exception($"Fatura reddedildi ({provider}/{invoiceRef ?? "?"}): {reason}")
{
    public string Provider { get; } = provider;
}

public readonly record struct ProcessInvoiceResult(
    ProviderInvoiceStatus Status, Guid? LedgerTransactionId, bool Replayed);
