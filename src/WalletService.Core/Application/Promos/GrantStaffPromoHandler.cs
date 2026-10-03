using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HiWallet.WalletService.Application.Promos;

/// <summary>
/// Personel promo'su: çalışanın müşteriye platform fonlu promo vermesi (decisions.md
/// madde 37). Kapsamı çalışan seçiyor; tutar para birimi başına tek seferlik tavanla
/// sınırlı.
/// </summary>
/// <param name="WalletId">Promo'yu alan müşteri cüzdanı. Idempotency kapsamı da bu cüzdan.</param>
/// <param name="MerchantAccountIds">Seçili işyerleri kapsamında işyeri hesapları; her yerde geçerli kapsamda boş.</param>
/// <param name="EmployeeSubject">Veren çalışanın <c>sub</c>'ı; ledger'da aktör.</param>
public sealed record GrantStaffPromoCommand(
    Guid WalletId,
    decimal Amount,
    string Currency,
    PromoScope Scope,
    IReadOnlyList<Guid> MerchantAccountIds,
    DateTimeOffset? ExpiresAt,
    string EmployeeSubject,
    string IdempotencyKey);

/// <summary>
/// Tek ACID transaction: ledger işlemi, parti ve iki bakiye satırı birlikte yazılıyor.
/// Ledger: <c>promo_expense</c> <c>-X</c>, müşteri <c>promo</c> <c>+X</c>.
/// </summary>
public sealed class GrantStaffPromoHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock,
    IOptions<StaffPromoOptions> options,
    ILogger<GrantStaffPromoHandler> logger)
{
    /// <summary>decisions.md madde 9: 3 deneme, her denemede yeniden oku.</summary>
    private const int MaxAttempts = 3;

    public async Task<GrantPromoResult> HandleAsync(GrantStaffPromoCommand command, CancellationToken ct)
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
                    "Personel promo'su çakışması, yeniden deneniyor. Deneme {Attempt}/{Max}, cüzdan {WalletId}",
                    attempt, MaxAttempts, command.WalletId);
            }
            catch (DbUpdateException exception) when (IsIdempotencyConflict(exception) && attempt < MaxAttempts)
            {
                logger.LogInformation(
                    "Personel promo'su isteği tekrar; mevcut parti dönülecek. Cüzdan {WalletId}, anahtar {Key}",
                    command.WalletId, command.IdempotencyKey);
            }
        }
    }

    private async Task<GrantPromoResult> AttemptAsync(GrantStaffPromoCommand command, CancellationToken ct)
    {
        var currency = Currency.From(command.Currency);
        var amount = new Money(command.Amount, currency);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var wallet = await db.LedgerAccounts.FirstOrDefaultAsync(
                         a => a.Id == command.WalletId && a.Type == LedgerAccountType.UserWallet, ct)
                     ?? throw new WalletNotFoundException(command.WalletId);

        // Idempotency kapısı kurallardan ÖNCE (decisions.md madde 21): tekrar eden istek
        // tavan ya da kapsam değişmiş olsa da mevcut partiyi dönmeli.
        var existing = await db.LedgerTransactions
            .AsNoTracking()
            .Where(t => t.LedgerAccountId == wallet.Id && t.IdempotencyKey == command.IdempotencyKey)
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

        if (!options.Value.MaxAmount.TryGetValue(currency.Code, out var max))
        {
            throw new PromoGrantRejectedException($"{currency.Code} için personel promo'su tanımlı değil.");
        }

        if (command.Amount > max)
        {
            throw new PromoGrantRejectedException(
                $"Personel promo'su tek seferde en fazla {max} {currency.Code}; daha büyük tutar kampanyayla verilir.");
        }

        if (wallet.Currency != currency)
        {
            throw new PromoGrantRejectedException($"Cüzdan {wallet.Currency.Code} tutuyor, promo {currency.Code}.");
        }

        var ownerType = await db.Accounts
            .Where(a => a.Id == wallet.AccountId)
            .Select(a => a.Type)
            .SingleAsync(ct);

        if (ownerType is not AccountType.Person)
        {
            throw new PromoGrantRejectedException("Personel promo'su yalnızca bireysel müşteriye verilir.");
        }

        var merchants = command.MerchantAccountIds.Distinct().ToArray();
        var businesses = await db.Accounts
            .CountAsync(a => merchants.Contains(a.Id) && a.Type == AccountType.Business, ct);

        if (businesses != merchants.Length)
        {
            throw new PromoGrantRejectedException("Kapsamdaki her hesap bir işyeri hesabı olmalı.");
        }

        var expense = await db.LedgerAccounts
                          .Where(a => a.Type == LedgerAccountType.PromoExpense && a.Currency == currency)
                          .Select(a => (Guid?)a.Id)
                          .SingleOrDefaultAsync(ct)
                      ?? throw new PromoGrantRejectedException($"{currency.Code} için promo gider hesabı yok.");

        var now = clock.UtcNow;

        var tx = LedgerTransaction
            .Create(Guid.NewGuid(), LedgerTransactionType.PromoGrant, wallet.Id,
                Actor.Employee(command.EmployeeSubject), now, command.IdempotencyKey)
            .AddEntry(expense, amount.Negated, FundType.Promo)
            .AddEntry(wallet.Id, amount, FundType.Promo);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        var grant = PromoGrant.ByStaff(
            Guid.NewGuid(), wallet.Id, amount, command.Scope, merchants, command.ExpiresAt, tx.Id, now);

        db.PromoGrants.Add(grant);

        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8). promo_expense
        // sistem hesabı, negatife düşebiliyor.
        foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
        {
            var balance = await db.LedgerBalances.SingleAsync(
                b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct);

            balance.Apply(entry.Money, canGoNegative: entry.LedgerAccountId != wallet.Id, now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new GrantPromoResult(grant.Id, Replayed: false);
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "ux_ledger_tx_idem";
}
