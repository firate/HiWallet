using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HiWallet.WalletService.Application.Deposits;

/// <param name="SuspendedDepositId">Askı kaydının ledger işlemi.</param>
/// <param name="EmployeeSubject">Kararı veren çalışanın <c>sub</c>'ı; ledger'da aktör.</param>
/// <param name="IdempotencyKey">Ledger anahtarı; kapsamı hedef cüzdan.</param>
public sealed record MoveSuspendedDepositCommand(
    Guid SuspendedDepositId,
    AccountNumber AccountNumber,
    string EmployeeSubject,
    string IdempotencyKey);

public sealed record MoveSuspendedDepositResult(
    Guid SuspendedDepositId,
    Guid AccountId,
    Guid WalletId,
    Guid LedgerTransactionId,
    bool Replayed);

/// <summary>
/// Askıdaki havaleyi bir hesabın varsayılan cüzdanına aktarır. Kararı çalışan veriyor:
/// açıklamada numara yoktu ya da gönderen hesabın sahibi değildi, çalışan paranın kime ait
/// olduğunu başka yoldan biliyor. Havalenin cüzdana geçme kuralının geri kalanı aynen
/// geçerli: hesap bireysel, bu para biriminde varsayılan cüzdanı var ve seviyenin limiti
/// yetiyor. Limiti aşan para aktarılamıyor, yalnızca kaynağına iade edilebilir.
///
/// Ledger: yükleme (<see cref="LedgerTransactionType.Topup"/>), cüzdan +, askı −; aktör
/// çalışan. Tip yükleme olduğu için ayın girişine kendiliğinden sayılıyor.
///
/// Tek ACID transaction: karar kaydı, limit ve ledger birlikte. Karar kaydı kapı: havale
/// başına bir satır, <c>INSERT ... ON CONFLICT DO NOTHING</c>; ikinci karar oraya takılıyor.
/// </summary>
public sealed class MoveSuspendedDepositHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    KycLimitPolicy kycLimits,
    IClock clock,
    ILogger<MoveSuspendedDepositHandler> logger)
{
    /// <summary>
    /// Aynı bankanın askıdaki bütün havaleleri aynı askı satırını güncelliyor; eşzamanlı
    /// ikisinden birini optimistic lock reddediyor. Taze okumayla yeniden deneniyor
    /// (decisions.md madde 9).
    /// </summary>
    private const int MaxAttempts = 3;

    public async Task<MoveSuspendedDepositResult> HandleAsync(MoveSuspendedDepositCommand command, CancellationToken ct)
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
                    "Askıdaki havale aktarılırken çakışma, yeniden deneniyor. {DepositId}, deneme {Attempt}/{Max}",
                    command.SuspendedDepositId, attempt, MaxAttempts);
            }
        }
    }

    private async Task<MoveSuspendedDepositResult> AttemptAsync(MoveSuspendedDepositCommand command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var deposit = await db.SuspendedDeposits
                          .AsNoTracking()
                          .SingleOrDefaultAsync(d => d.LedgerTransactionId == command.SuspendedDepositId, ct)
                      ?? throw new SuspendedDepositNotFoundException(command.SuspendedDepositId);

        var account = await db.Accounts
                          .AsNoTracking()
                          .Where(a => a.Number == command.AccountNumber)
                          .Select(a => new { a.Id, a.Type, a.Holder, a.KycLevel })
                          .SingleOrDefaultAsync(ct)
                      ?? throw new AccountNumberNotFoundException(command.AccountNumber);

        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        // --- Karar kapısı -------------------------------------------------------------
        // Kurallardan ÖNCE: tekrar eden istek limit değişmiş olsa da aynı cevabı almalı.
        // Kural reddinde transaction geri alınıyor, satır da onunla gidiyor.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO suspended_deposit_resolutions
                 (suspended_deposit_id, kind, ledger_transaction_id, account_id, resolved_by, created_at)
             VALUES ({deposit.LedgerTransactionId}, 'moved', {transactionId}, {account.Id}, {command.EmployeeSubject}, {now})
             ON CONFLICT (suspended_deposit_id) DO NOTHING
             """,
            ct);

        if (inserted == 0)
        {
            var existing = await (
                    from r in db.SuspendedDepositResolutions
                    join t in db.LedgerTransactions on r.LedgerTransactionId equals t.Id
                    where r.SuspendedDepositId == deposit.LedgerTransactionId
                    select new { r.AccountId, t.LedgerAccountId, t.Id, t.IdempotencyKey })
                .SingleAsync(ct);

            await transaction.RollbackAsync(ct);

            if (existing.IdempotencyKey != command.IdempotencyKey)
            {
                throw new DepositResolutionRejectedException(
                    DepositResolutionRejectedException.AlreadyResolved, "Bu havale için karar verilmiş.");
            }

            return new MoveSuspendedDepositResult(
                deposit.LedgerTransactionId, existing.AccountId, existing.LedgerAccountId, existing.Id, Replayed: true);
        }

        // --- Hedef --------------------------------------------------------------------
        // Havalenin cüzdana geçme kuralıyla aynı (ProcessDepositHandler): işyerinin seviyesi
        // yok, limiti kontrol edilemez.
        if (account.Type is not AccountType.Person || account.Holder is null || account.KycLevel is null)
        {
            throw new DepositResolutionRejectedException(
                DepositResolutionRejectedException.TargetNotPerson, "Havale yalnızca bireysel hesabın cüzdanına aktarılır.");
        }

        var amount = deposit.Money;

        var walletId = await db.DefaultWallets
                           .Where(d => d.AccountId == account.Id && d.Currency == amount.Currency)
                           .Select(d => (Guid?)d.WalletId)
                           .SingleOrDefaultAsync(ct)
                       ?? throw new NoWalletInCurrencyException(amount.Currency);

        // --- Limit --------------------------------------------------------------------
        // Havalenin kendi yolundakiyle aynı sayım: ayın toplam girişi ve bakiye tavanı,
        // hesabın satırı kilitli.
        await IncomingUsage.LockAsync(db, account.Id, ct);

        var received = await IncomingUsage.ThisMonthAsync(db, account.Id, amount.Currency, now, ct);
        var balance = await IncomingUsage.BalanceAsync(db, account.Id, amount.Currency, ct);

        kycLimits.EnsureIncoming(account.KycLevel.Value, KycMovement.Deposit, amount, received, balance);

        // --- Ledger -------------------------------------------------------------------
        var suspense = await db.LedgerAccounts
            .AsNoTracking()
            .SingleAsync(a => a.Type == LedgerAccountType.Suspense
                              && a.Provider == deposit.Provider
                              && a.Currency == amount.Currency, ct);

        // Anahtar başka bir işlemde kullanılmışsa unique index reddederdi; çalışana
        // anlaşılır bir sebep dönmek için önce bakılıyor.
        if (await db.LedgerTransactions.AnyAsync(
                t => t.LedgerAccountId == walletId && t.IdempotencyKey == command.IdempotencyKey, ct))
        {
            throw new DepositResolutionRejectedException(
                DepositResolutionRejectedException.KeyReused,
                "Bu Idempotency-Key bu cüzdanda başka bir işlemde kullanılmış.");
        }

        var tx = LedgerTransaction
            .Create(transactionId, LedgerTransactionType.Topup, walletId,
                Actor.Employee(command.EmployeeSubject), now, command.IdempotencyKey)
            .AddEntry(walletId, amount, FundType.Cash)
            .AddEntry(suspense.Id, amount.Negated, FundType.Cash);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // --- Projeksiyon --------------------------------------------------------------
        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8). Askı negatife
        // düşemiyor: havale ancak oradaysa aktarılır.
        foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
        {
            var row = await db.LedgerBalances.SingleAsync(
                b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct);

            row.Apply(entry.Money, canGoNegative: false, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsKeyConflict(exception))
        {
            throw new DepositResolutionRejectedException(
                DepositResolutionRejectedException.KeyReused,
                "Bu Idempotency-Key bu cüzdanda başka bir işlemde kullanılmış.");
        }

        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Askıdaki havale cüzdana aktarıldı. {DepositId} → {TransactionId}, hesap {AccountId}, {Amount} {Currency}",
            deposit.LedgerTransactionId, transactionId, account.Id, amount.Amount, amount.Currency.Code);

        return new MoveSuspendedDepositResult(deposit.LedgerTransactionId, account.Id, walletId, transactionId, Replayed: false);
    }

    private static bool IsKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "ux_ledger_tx_idem";
}
