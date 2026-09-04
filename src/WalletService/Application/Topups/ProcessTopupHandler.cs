using HiWallet.Shared.Contracts.Topups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Topups;

/// <summary>
/// Dışarıdan gelen para girişini ledger'a yazar (overview.md madde 5).
/// Saga DEĞİL: tek adım, geri alınacak bir şey yok.
///
/// Sıra: <b>idempotency kapısı → ledger → projeksiyon</b>, hepsi TEK transaction.
/// Kapının ledger ile aynı transaction'da olması şart — ayrı commit'lerde yazılsaydı
/// aradaki çökme ya "işlendi ama para yatmadı" ya da "para yattı ama işlenmedi
/// sayıldı, ikinci teslimde bir kez daha yattı" bırakırdı.
///
/// Ledger etkisi <c>ledger-schema.md</c> "Top-up": cüzdan <c>+X</c>, ilgili
/// sağlayıcının <c>clearing</c>'i <c>−X</c> (sağlayıcıdan alacak). Settlement geldiğinde
/// clearing ayrı bir işlemle kapanıyor; bu handler oraya karışmıyor.
/// </summary>
public sealed class ProcessTopupHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock,
    ILogger<ProcessTopupHandler> logger)
{
    public async Task<ProcessTopupResult> HandleAsync(TopupReceived message, CancellationToken ct)
    {
        var currency = ParseCurrency(message);
        var amount = ParseAmount(message, currency);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var wallet = await LoadWalletAsync(db, message, currency, ct);
        var clearing = await LoadClearingAsync(db, message, currency, ct);

        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        // --- Idempotency kapısı ----------------------------------------------------
        // "Önce SELECT sonra INSERT" YOK (CLAUDE.md): iki tüketici arasında TOCTOU
        // açığı var. ON CONFLICT DO NOTHING kararı tek adımda DB'ye verdiriyor.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO processed_events (provider, event_id, processed_at, ledger_transaction_id)
             VALUES ({message.Provider}, {message.EventId}, {now}, {transactionId})
             ON CONFLICT (provider, event_id) DO NOTHING
             """,
            ct);

        if (inserted == 0)
        {
            var original = await db.ProcessedEvents
                .AsNoTracking()
                .Where(e => e.Provider == message.Provider && e.EventId == message.EventId)
                .Select(e => e.LedgerTransactionId)
                .FirstAsync(ct);

            await transaction.RollbackAsync(ct);

            logger.LogInformation(
                "Top-up zaten işlenmiş, atlanıyor. {Provider}/{EventId} → {TransactionId}",
                message.Provider, message.EventId, original);

            return new ProcessTopupResult(original, Replayed: true);
        }

        // --- Ledger ----------------------------------------------------------------
        // Idempotency kapsamı alıcı cüzdan, key ise webhook event_id (decisions.md
        // madde 15). processed_events'e ek olarak buradaki unique index de aynı
        // event'in ikinci kez yazılmasını engelliyor — ikinci bir emniyet kemeri.
        var tx = LedgerTransaction.Create(
            transactionId, LedgerTransactionType.Topup, wallet.Id, now, message.EventId);

        tx.AddEntry(wallet.Id, amount);
        tx.AddEntry(clearing.Id, amount.Negated);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // --- Projeksiyon -------------------------------------------------------------
        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8).
        var deltas = tx.Entries
            .Select(e => (e.LedgerAccountId, e.Money))
            .OrderBy(x => x.LedgerAccountId)
            .ToArray();

        var canGoNegative = new Dictionary<Guid, bool>
        {
            [wallet.Id] = wallet.CanGoNegative,
            [clearing.Id] = clearing.CanGoNegative
        };

        foreach (var (ledgerAccountId, delta) in deltas)
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(b => b.LedgerAccountId == ledgerAccountId, ct)
                          ?? throw new InvalidOperationException(
                              $"Bakiye satırı yok: {ledgerAccountId}");

            balance.Apply(delta, canGoNegative[ledgerAccountId], now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Top-up işlendi. {Provider}/{EventId} → {TransactionId}, cüzdan {WalletId} +{Amount} {Currency}",
            message.Provider, message.EventId, transactionId, wallet.Id, amount.Amount, currency.Code);

        return new ProcessTopupResult(transactionId, Replayed: false);
    }

    private static Currency ParseCurrency(TopupReceived message)
    {
        try
        {
            return Currency.From(message.Currency);
        }
        catch (ArgumentException exception)
        {
            throw new TopupRejectedException(message.Provider, message.EventId, exception.Message);
        }
    }

    private static Money ParseAmount(TopupReceived message, Currency currency)
    {
        if (message.Amount <= 0m)
        {
            throw new TopupRejectedException(
                message.Provider, message.EventId, $"Tutar pozitif olmalı: {message.Amount}");
        }

        try
        {
            return new Money(message.Amount, currency);
        }
        catch (ArgumentException exception)
        {
            // Para biriminin küsurat basamağından fazla ondalık. Sağlayıcı yanlış
            // gönderiyor; yeniden denemek düzeltmez.
            throw new TopupRejectedException(message.Provider, message.EventId, exception.Message);
        }
    }

    private static async Task<LedgerAccount> LoadWalletAsync(
        WalletDbContext db, TopupReceived message, Currency currency, CancellationToken ct)
    {
        var wallet = await db.LedgerAccounts
                         .FirstOrDefaultAsync(a => a.Id == message.LedgerAccountId, ct)
                     ?? throw new TopupRejectedException(
                         message.Provider, message.EventId, $"Cüzdan yok: {message.LedgerAccountId}");

        if (wallet.Type is not LedgerAccountType.UserWallet)
        {
            throw new TopupRejectedException(
                message.Provider, message.EventId,
                $"{wallet.Id} bir cüzdan değil, {wallet.Type} sistem hesabı.");
        }

        if (wallet.Currency != currency)
        {
            throw new TopupRejectedException(
                message.Provider, message.EventId,
                $"Cüzdan {wallet.Currency} tutuyor, {currency} geldi.");
        }

        return wallet;
    }

    /// <summary>
    /// Sağlayıcının clearing hesabı. Sabit kimlik kullanılmıyor: clearing sağlayıcı
    /// BAŞINA ayrı (decisions.md madde 14) ve yeni bir sağlayıcı eklendiğinde burada
    /// kod değişmemeli — seed'e satır eklemek yetmeli.
    /// </summary>
    private static async Task<LedgerAccount> LoadClearingAsync(
        WalletDbContext db, TopupReceived message, Currency currency, CancellationToken ct)
    {
        return await db.LedgerAccounts
                   .FirstOrDefaultAsync(
                       a => a.Type == LedgerAccountType.Clearing
                            && a.Provider == message.Provider
                            && a.Currency == currency,
                       ct)
               ?? throw new TopupRejectedException(
                   message.Provider, message.EventId,
                   $"'{message.Provider}' sağlayıcısının {currency} clearing hesabı yok.");
    }
}
