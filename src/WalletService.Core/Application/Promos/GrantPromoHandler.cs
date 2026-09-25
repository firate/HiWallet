using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HiWallet.WalletService.Application.Promos;

/// <summary>
/// İşyerinin kendi müşterisine promo vermesi (decisions.md madde 37). Tek ACID
/// transaction: ledger işlemi, parti ve iki bakiye satırı birlikte yazılıyor.
///
/// Ledger: işyeri <c>cash</c> <c>-X</c>, müşteri <c>promo</c> <c>+X</c>.
/// </summary>
public sealed class GrantPromoHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock,
    ILogger<GrantPromoHandler> logger)
{
    /// <summary>decisions.md madde 9: 3 deneme, her denemede yeniden oku.</summary>
    private const int MaxAttempts = 3;

    public async Task<GrantPromoResult> HandleAsync(GrantPromoCommand command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AttemptAsync(command, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogDebug(
                    "Promo yükleme çakışması, yeniden deneniyor. Deneme {Attempt}/{Max}, cüzdan {WalletId}",
                    attempt, MaxAttempts, command.FunderWalletId);
            }
            catch (DbUpdateException exception) when (IsIdempotencyConflict(exception) && attempt < MaxAttempts)
            {
                // Aynı anahtarla eşzamanlı iki istek kapıdan birlikte geçti; tekilliğe
                // unique index karar verdi. Sonraki deneme kapıda mevcut partiyi buluyor.
                logger.LogInformation(
                    "Promo yükleme isteği tekrar; mevcut parti dönülecek. Cüzdan {WalletId}, anahtar {Key}",
                    command.FunderWalletId, command.IdempotencyKey);
            }
        }
    }

    private async Task<GrantPromoResult> AttemptAsync(GrantPromoCommand command, CancellationToken ct)
    {
        var currency = Currency.From(command.Currency);
        var amount = new Money(command.Amount, currency);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var funder = await LoadWalletAsync(db, command.FunderWalletId, currency, ct);
        var wallet = await LoadWalletAsync(db, command.WalletId, currency, ct);

        // Idempotency kapısı kurallardan ÖNCE (decisions.md madde 21): tekrar eden
        // istek hiçbir kuralı yeniden değerlendirmeden mevcut partiyi dönmeli.
        var existing = await db.LedgerTransactions
            .AsNoTracking()
            .Where(t => t.LedgerAccountId == funder.Id && t.IdempotencyKey == command.IdempotencyKey)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is { } existingTxId)
        {
            var grantId = await db.PromoGrants
                              .Where(g => g.LedgerTransactionId == existingTxId)
                              .Select(g => (Guid?)g.Id)
                              .SingleOrDefaultAsync(ct)
                          ?? throw new PromoGrantRejectedException(
                              "Bu Idempotency-Key bu cüzdanda başka bir işlemde kullanılmış.");

            return new GrantPromoResult(grantId, Replayed: true);
        }

        var funderAccountId = funder.AccountId
                              ?? throw new InvalidOperationException($"Cüzdan {funder.Id} bir hesaba bağlı değil.");

        var funderType = await db.Accounts
            .Where(a => a.Id == funderAccountId)
            .Select(a => a.Type)
            .SingleAsync(ct);

        if (funderType is not AccountType.Business)
        {
            throw new PromoGrantRejectedException("Promo'yu yalnızca işyeri hesabı fonlayabilir.");
        }

        if (wallet.AccountId == funderAccountId)
        {
            throw new PromoGrantRejectedException("İşyeri kendi hesabına promo veremez.");
        }

        var now = clock.UtcNow;

        // Yalnızca cash: kart parası promo'yu fonlayabilseydi işyerinde harcanan promo
        // işyerinin cash kovasına dönüp IBAN'a çıkardı (decisions.md madde 37).
        var tx = LedgerTransaction
            .Create(Guid.NewGuid(), LedgerTransactionType.PromoGrant, funder.Id,
                Actor.Customer(funderAccountId), now, command.IdempotencyKey)
            .AddEntry(funder.Id, amount.Negated, FundType.Cash)
            .AddEntry(wallet.Id, amount, FundType.Promo);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        var grant = PromoGrant.FundedByBusiness(
            Guid.NewGuid(), wallet.Id, amount, funder.Id, funderAccountId, command.ExpiresAt, tx.Id, now);

        db.PromoGrants.Add(grant);

        // Bakiye ledger'la aynı transaction'da; sıra ledger hesap kimliğine göre ARTAN
        // (decisions.md madde 8). İki bacak da cüzdan, ikisi de negatife düşemez.
        foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(
                                  b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct)
                          ?? throw new InvalidOperationException(
                              $"Bakiye satırı yok: {entry.LedgerAccountId} / {entry.FundType}");

            balance.Apply(entry.Money, canGoNegative: false, now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new GrantPromoResult(grant.Id, Replayed: false);
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
    /// Yalnızca idempotency ihlali tekrar sayılıyor; başka bir unique ya da FK hatası
    /// gerçek bir arıza ve yukarı çıkmalı.
    /// </summary>
    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "ux_ledger_tx_idem";
}
