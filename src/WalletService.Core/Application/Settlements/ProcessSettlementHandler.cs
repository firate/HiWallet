using HiWallet.Shared.Contracts.Settlements;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Settlements;

/// <summary>
/// Sağlayıcının batch ödemesini ledger'a yazar (<c>ledger-schema.md</c>
/// "Settlement kayıtları"). Top-up'ta açılan alacağı kapatıyor ve parayı
/// <c>nostro</c>'ya taşıyor.
///
/// <code>
/// clearing         +100    alacak kapanır
/// provider_expense  -2.9   yalnızca Net modelde
/// nostro           -97.1   banka hesabına giren gerçek tutar
/// </code>
///
/// <b>İşaretler.</b> <c>nostro</c> bir VARLIK hesabı ve bu ledger'da varlıklar
/// negatif duruyor: <c>-97.1</c> "97.1 açık" değil, "bankada 97.1 var" demek
/// (<c>ledger-schema.md</c> "İşaret konvansiyonu"). Ters çevirmek dengeli ama
/// anlamsız bir kayıt üretirdi.
///
/// <b>Invoiced modelde <c>provider_expense</c> bacağı YOK</b> (madde 10): ücret o
/// modelde settlement'ta değil, dönem sonu faturasında ödeniyor. Bacağı yine de
/// yazmak gideri iki kez kaydederdi — bir kez burada, bir kez faturada.
///
/// <b>Sıra: idempotency kapısı → ledger → projeksiyon</b>, hepsi TEK transaction.
/// Top-up handler'ıyla aynı gerekçe.
/// </summary>
public sealed class ProcessSettlementHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    ProviderPolicy providers,
    IClock clock,
    ILogger<ProcessSettlementHandler> logger)
{
    public async Task<ProcessSettlementResult> HandleAsync(
        SettlementReceived message, CancellationToken ct)
    {
        var currency = Currency.From(message.Currency);
        var terms = providers.For(message.Provider);

        Validate(message, terms);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var clearing = await LoadSystemAccountAsync(
            db, LedgerAccountType.Clearing, message.Provider, currency, ct);

        var nostro = await LoadNostroAsync(db, message, currency, ct);

        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        // --- Idempotency kapısı ----------------------------------------------------
        // Top-up ile AYNI tablo ve aynı kalıp: settlement de sağlayıcıdan gelen bir
        // event ve tekilliği (provider, event_id). Ayrı bir tablo açmak, aynı
        // "ON CONFLICT DO NOTHING" mantığını ikinci kez doğru yazmayı gerektirirdi.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO processed_events (provider, event_id, processed_at, ledger_transaction_id)
             VALUES ({message.Provider}, {message.SettlementId}, {now}, {transactionId})
             ON CONFLICT (provider, event_id) DO NOTHING
             """,
            ct);

        if (inserted == 0)
        {
            var original = await db.ProcessedEvents
                .AsNoTracking()
                .Where(e => e.Provider == message.Provider && e.EventId == message.SettlementId)
                .Select(e => e.LedgerTransactionId)
                .FirstAsync(ct);

            await transaction.RollbackAsync(ct);

            logger.LogInformation(
                "Settlement zaten işlenmiş, atlanıyor. {Provider}/{SettlementId} → {TransactionId}",
                message.Provider, message.SettlementId, original);

            return new ProcessSettlementResult(original, Replayed: true);
        }

        // --- Ledger ----------------------------------------------------------------
        // Idempotency kapsamı clearing hesabı, key ise sağlayıcının batch referansı
        // (ledger-schema.md "settlement" satırı). Cüzdan yok — settlement hiçbir
        // müşteri hesabına dokunmuyor.
        var tx = LedgerTransaction.Create(
            transactionId,
            LedgerTransactionType.Settlement,
            clearing.Id,
            now,
            IdempotencyKey(message));

        tx.AddEntry(clearing.Id, new Money(message.GrossAmount, currency));
        tx.AddEntry(nostro.Id, new Money(-message.NetAmount, currency));

        if (terms.FeeSettlement is FeeSettlement.Net && message.FeeAmount > 0m)
        {
            var expense = await LoadSystemAccountAsync(
                db, LedgerAccountType.ProviderExpense, message.Provider, currency, ct);

            tx.AddEntry(expense.Id, new Money(-message.FeeAmount, currency));
        }

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // --- Ücret satırlarının kapanması (ledger DEĞİL) -----------------------------
        var matched = await CloseFeesAsync(db, message, transactionId, terms, ct);

        // --- Projeksiyon -------------------------------------------------------------
        await ApplyBalancesAsync(db, tx, now, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Settlement işlendi. {Provider}/{SettlementId} → {TransactionId}, " +
            "brüt {Gross}, ücret {Fee}, net {Net} {Currency}; {Matched} ücret satırı kapandı.",
            message.Provider, message.SettlementId, transactionId,
            message.GrossAmount, message.FeeAmount, message.NetAmount, currency.Code, matched);

        return new ProcessSettlementResult(transactionId, Replayed: false);
    }

    /// <summary>
    /// Ledger'a yazılan idempotency key. Sağlayıcı öneki ZORUNLU: tekillik
    /// <c>(ledger_account_id, idempotency_key)</c> üzerinde ve iki sağlayıcı aynı
    /// batch numarasını üretebilir. Top-up'taki aynı tuzak (madde 27).
    /// </summary>
    public static string IdempotencyKey(SettlementReceived message) =>
        $"{message.Provider}:settlement:{message.SettlementId}";

    private static void Validate(SettlementReceived message, ProviderTerms terms)
    {
        // Sınırda da doğrulanıyor ama burası tek savunma hattı değil: mesaj kuyruktan
        // da gelebiliyor ve webhook dışında bir üretici eklendiğinde bu kontrol
        // yerinde kalmalı.
        if (message.GrossAmount != message.NetAmount + message.FeeAmount)
        {
            throw new SettlementRejectedException(
                message.Provider, message.SettlementId,
                $"Brüt {message.GrossAmount}, net {message.NetAmount} + ücret {message.FeeAmount} " +
                "toplamına eşit değil.");
        }

        // Invoiced modelde ücret settlement'ta kesilmiyor. Sıfırdan farklı gelmesi
        // ya sağlayıcının modeli değiştirdiği ya da konfigürasyonun eskidiği anlamına
        // geliyor; ikisi de sessizce yanlış gider kaydı üretirdi.
        if (terms.FeeSettlement is FeeSettlement.Invoiced && message.FeeAmount != 0m)
        {
            throw new SettlementRejectedException(
                message.Provider, message.SettlementId,
                $"Sağlayıcı Invoiced modelde ama settlement'ta {message.FeeAmount} ücret bildirdi. " +
                "Ücret dönem sonu faturasıyla alınmalıydı.");
        }
    }

    /// <summary>
    /// Batch'in kapsadığı ücret satırlarını kapatır: <c>actual_amount</c> dolar,
    /// <c>ledger_tx_id</c> settlement kaydına bağlanır.
    ///
    /// Ücret satır bazına PAYLAŞTIRILMIYOR. Sağlayıcı toplam kesintiyi bildiriyor;
    /// satır başına dağıtmak uydurma bir hassasiyet üretir ve yuvarlama farkları
    /// toplamı tutturmaz. Beklenen–gerçekleşen karşılaştırması zaten batch
    /// seviyesinde anlamlı (madde 11).
    /// </summary>
    private async Task<int> CloseFeesAsync(
        WalletDbContext db,
        SettlementReceived message,
        Guid transactionId,
        ProviderTerms terms,
        CancellationToken ct)
    {
        var refs = message.ProviderRefs.ToArray();

        var fees = await db.ProviderFees
            .Where(f => f.Provider == message.Provider
                        && f.ProviderRef != null
                        && refs.Contains(f.ProviderRef)
                        && f.ActualAmount == null)
            .ToListAsync(ct);

        if (fees.Count == 0)
        {
            // Kapatılacak satır yok. Ledger yine yazılıyor — para gerçekten geldi.
            // Ama bu bir uyarı: batch bizde olmayan işlemleri kapsıyor demek
            // (kaçırılmış webhook) ve mutabakat bunu ayrıca raporlayacak (5.7).
            logger.LogWarning(
                "Settlement {Provider}/{SettlementId} hiçbir ücret satırıyla eşleşmedi. " +
                "Kapsanan referans sayısı: {Count}",
                message.Provider, message.SettlementId, refs.Length);

            return 0;
        }

        // Net modelde gerçekleşen ücret biliniyor; Invoiced modelde HENÜZ değil,
        // fatura gelene kadar NULL kalıyor (madde 10). Satırlar yine de settlement
        // kaydına bağlanıyor: hangi batch'te kapandıkları fatura eşleştirmesinin
        // girdisi.
        var actualPerFee = terms.FeeSettlement is FeeSettlement.Net
            ? message.FeeAmount / fees.Count
            : (decimal?)null;

        foreach (var fee in fees)
        {
            fee.LedgerTransactionId = transactionId;

            if (actualPerFee is not null)
            {
                fee.ActualAmount = decimal.Round(actualPerFee.Value, 4);
            }
        }

        return fees.Count;
    }

    private static async Task ApplyBalancesAsync(
        WalletDbContext db, LedgerTransaction tx, DateTimeOffset now, CancellationToken ct)
    {
        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8).
        var deltas = tx.Entries
            .Select(e => (e.LedgerAccountId, e.Money))
            .OrderBy(x => x.LedgerAccountId)
            .ToArray();

        foreach (var (ledgerAccountId, delta) in deltas)
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(b => b.LedgerAccountId == ledgerAccountId, ct)
                          ?? throw new InvalidOperationException(
                              $"Bakiye satırı yok: {ledgerAccountId}");

            // Hepsi sistem hesabı; üçü de negatife düşebilir (decisions.md madde 6).
            balance.Apply(delta, canGoNegative: true, now);
        }
    }

    /// <summary>
    /// Paranın gireceği kendi banka hesabımız.
    ///
    /// <c>clearing</c> ve <c>provider_expense</c>'in aksine <c>nostro</c> ödeme
    /// sağlayıcısına göre anahtarlanmıyor: <c>provider</c> kolonu orada parayı TUTAN
    /// bankayı söylüyor (<c>nostro/garanti</c>, decisions.md madde 14). Stripe'ın
    /// gönderdiği para da bizim banka hesabımıza giriyor, "stripe nostro"su diye bir
    /// şey yok.
    ///
    /// Bugün para birimi başına tek nostro var, o yüzden eşleme sorusuz. İkinci bir
    /// banka eklendiğinde hangi settlement'ın hangi hesaba düştüğü KONFİGÜRASYON
    /// sorusu olacak; o güne kadar sessizce yanlış hesap seçmektense burada
    /// patlıyor.
    /// </summary>
    private static async Task<LedgerAccount> LoadNostroAsync(
        WalletDbContext db, SettlementReceived message, Currency currency, CancellationToken ct)
    {
        var candidates = await db.LedgerAccounts
            .Where(a => a.Type == LedgerAccountType.Nostro && a.Currency == currency)
            .OrderBy(a => a.Provider)
            .Take(2)
            .ToListAsync(ct);

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new SettlementRejectedException(
                message.Provider, message.SettlementId, $"{currency} nostro hesabı yok."),
            _ => throw new SettlementRejectedException(
                message.Provider, message.SettlementId,
                $"{currency} için birden fazla nostro hesabı var; settlement'ın hangisine " +
                "düştüğü konfigürasyonla belirtilmeli.")
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
               ?? throw new SettlementRejectedException(
                   provider, settlementId: null,
                   $"'{provider}' sağlayıcısının {currency} {type} hesabı yok.");
    }
}

/// <summary>
/// Settlement kalıcı olarak işlenemez: tutarsız batch, eksik sistem hesabı, model
/// uyuşmazlığı. Dead-letter'a gidiyor — aynı mesajı yeniden denemek aynı sonucu
/// verir ve kuyruğu süresiz tıkardı.
/// </summary>
public sealed class SettlementRejectedException(string provider, string? settlementId, string reason)
    : Exception($"Settlement reddedildi ({provider}/{settlementId ?? "?"}): {reason}")
{
    public string Provider { get; } = provider;

    public string? SettlementId { get; } = settlementId;
}

public readonly record struct ProcessSettlementResult(Guid? LedgerTransactionId, bool Replayed);
