using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Transfers;

/// <summary>
/// Transfer çekirdeği: tek ACID transaction, optimistic lock, saga YOK (overview.md madde 4).
///
/// Akış sırası önemli ve <c>ledger-schema.md</c>'deki referans akıştan bir noktada
/// AYRILIYOR — idempotency kapısı policy'den önce. Gerekçe aşağıda.
/// </summary>
public sealed class CreateTransferHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    LimitPolicy limitPolicy,
    CommissionPolicy commissionPolicy,
    IClock clock,
    ILogger<CreateTransferHandler> logger)
{
    /// <summary>decisions.md madde 9: 3 deneme, her denemede yeniden oku.</summary>
    private const int MaxAttempts = 3;

    public async Task<TransferResult> HandleAsync(CreateTransferCommand command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AttemptAsync(command, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Eski version ile tekrar denemek sonsuza kadar başarısız olur; retry'ın
                // anlamı YENİ anlık görüntüyle yeniden denemek. Taze context, taze okuma,
                // komisyon ve limit yeniden hesaplanır (decisions.md madde 9).
                logger.LogDebug(
                    "Transfer çakışması, yeniden deneniyor. Deneme {Attempt}/{Max}, cüzdan {WalletId}",
                    attempt, MaxAttempts, command.FromWalletId);
            }
        }
    }

    private async Task<TransferResult> AttemptAsync(CreateTransferCommand command, CancellationToken ct)
    {
        var currency = Currency.From(command.Currency);
        var amount = new Money(command.Amount, currency);

        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Transfer tutarı pozitif olmalı.", nameof(command));
        }

        if (command.FromWalletId == command.ToWalletId)
        {
            throw new ArgumentException("Gönderen ve alan cüzdan aynı olamaz.", nameof(command));
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var sender = await LoadWalletAsync(db, command.FromWalletId, currency, ct);
        var receiver = await LoadWalletAsync(db, command.ToWalletId, currency, ct);

        // --- Idempotency kapısı: policy'den ÖNCE ---------------------------------
        // ledger-schema.md'nin referans akışı bunu policy'den SONRA gösteriyor ama bu
        // bozuk: ilk transfer günlük limiti doldurduysa aynı isteğin tekrarı limit
        // aşımına takılır ve orijinal işlemi dönmek yerine 422 verir. Tekrar eden istek
        // hiçbir kuralı yeniden değerlendirmemeli, sadece olanı dönmeli.
        if (command.IdempotencyKey is { } key)
        {
            var existing = await db.LedgerTransactions
                .AsNoTracking()
                .Where(t => t.LedgerAccountId == sender.Id && t.IdempotencyKey == key)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);

            if (existing is { } existingId)
            {
                return new TransferResult(existingId, Replayed: true);
            }
        }

        // --- Policy ---------------------------------------------------------------
        var commission = commissionPolicy.Calculate(command.Type, amount);
        var debit = amount + commission;

        var senderAccountId = sender.AccountId
                              ?? throw new InvalidOperationException(
                                  $"Cüzdan {sender.Id} bir hesaba bağlı değil.");

        var spentToday = await SpentTodayAsync(db, senderAccountId, command.Type, currency, ct);

        // Kapsam hesap, cüzdan değil: aksi halde ikinci cüzdan açılarak aşılır
        // (decisions.md madde 20). Limit cüzdandan ÇIKAN toplama uygulanıyor, yani
        // komisyon dahil — koruduğu şey "bu hesaptan bugün ne kadar para çıktı".
        limitPolicy.Ensure(senderAccountId, command.Type, debit, spentToday);

        // --- Ledger ---------------------------------------------------------------
        var now = clock.UtcNow;
        var tx = LedgerTransaction.Create(
            Guid.NewGuid(), command.Type.ToLedgerType(), sender.Id, now, command.IdempotencyKey);

        tx.AddEntry(sender.Id, debit.Negated);
        tx.AddEntry(receiver.Id, amount);

        if (!commission.IsZero)
        {
            // Komisyon ayrı bir transfer değil, aynı atomik işlemin ek bacağı.
            tx.AddEntry(SystemAccounts.RevenueTry, commission);
        }

        // DB'deki deferred trigger'dan önce, daha anlaşılır hatayla.
        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // --- Projeksiyon ----------------------------------------------------------
        // Bakiye asla ledger'a yazmadan güncellenmez (CLAUDE.md). Sıra ledger hesap
        // kimliğine göre ARTAN — A→B ve B→A eşzamanlı geldiğinde deadlock olmasın
        // (decisions.md madde 8).
        var deltas = tx.Entries
            .Select(e => (e.LedgerAccountId, e.Money))
            .OrderBy(x => x.LedgerAccountId)
            .ToArray();

        var affectedIds = deltas.Select(d => d.LedgerAccountId).ToArray();
        var affected = await db.LedgerAccounts
            .Where(a => affectedIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var (ledgerAccountId, delta) in deltas)
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(b => b.LedgerAccountId == ledgerAccountId, ct)
                          ?? throw new InvalidOperationException(
                              $"Bakiye satırı yok: {ledgerAccountId}");

            // Negatife düşebilirlik hesabın TİPİNDEN geliyor, transferdeki rolünden değil.
            // "Gönderen ve alan hariç herkes düşebilir" diye yazmak bugün doğru sonucu
            // verirdi ama kuralı yanlış yere bağlardı (decisions.md madde 6).
            balance.Apply(delta, affected[ledgerAccountId].CanGoNegative, now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new TransferResult(tx.Id, Replayed: false);
    }

    private static async Task<LedgerAccount> LoadWalletAsync(
        WalletDbContext db, Guid walletId, Currency currency, CancellationToken ct)
    {
        var wallet = await db.LedgerAccounts.FirstOrDefaultAsync(a => a.Id == walletId, ct)
                     ?? throw new WalletNotFoundException(walletId);

        if (wallet.Type is not LedgerAccountType.UserWallet)
        {
            throw new ArgumentException(
                $"{walletId} bir cüzdan değil, {wallet.Type} sistem hesabı.", nameof(walletId));
        }

        if (wallet.Currency != currency)
        {
            throw new ArgumentException(
                $"Cüzdan {walletId} {wallet.Currency} tutuyor, {currency} istendi.", nameof(currency));
        }

        return wallet;
    }

    /// <summary>
    /// Hesabın TÜM cüzdanlarından bugün bu tiple çıkan toplam. Tek cüzdandan toplansaydı
    /// limit ikinci cüzdan açılarak aşılırdı (decisions.md madde 20).
    /// </summary>
    private async Task<Money> SpentTodayAsync(
        WalletDbContext db, Guid accountId, TransferType type, Currency currency, CancellationToken ct)
    {
        var since = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var ledgerType = type.ToLedgerType();

        var walletIds = db.LedgerAccounts
            .Where(a => a.AccountId == accountId)
            .Select(a => a.Id);

        // Debit bacaklarının toplamı; işaret negatif olduğu için sonuç ters çevriliyor.
        var debited = await db.LedgerEntries
            .Where(e => walletIds.Contains(e.LedgerAccountId)
                        && e.Amount < 0m
                        && e.Currency == currency
                        && db.LedgerTransactions.Any(t => t.Id == e.TransactionId && t.Type == ledgerType)
                        && e.CreatedAt >= since)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new Money(-debited, currency);
    }
}
