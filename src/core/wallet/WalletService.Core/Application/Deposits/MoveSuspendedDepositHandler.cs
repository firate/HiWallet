using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Deposits;
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
/// Tek ACID transaction: karar adımı, limit ve ledger birlikte. Adım kapı
/// (<see cref="SuspendedDepositSteps"/>): aynı anda verilen ikinci karar ona takılıyor.
/// İadesi süren havale aktarılamıyor; iadesi geri konan aktarılabiliyor.
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
        // Kural reddinde transaction geri alınıyor, adım da onunla gidiyor.
        var last = await SuspendedDepositSteps.LastAsync(db, deposit.LedgerTransactionId, ct);

        if (!SuspendedDepositResolution.IsOpenAfter(last?.Kind))
        {
            await transaction.RollbackAsync(ct);

            return await ReplayOrRejectAsync(db, deposit.LedgerTransactionId, last!, command, ct);
        }

        if (!await SuspendedDepositSteps.AppendAsync(
                db, deposit.LedgerTransactionId, last, DepositResolutionKind.Moved, transactionId, account.Id,
                command.EmployeeSubject, now, ct))
        {
            throw SuspendedDepositSteps.Raced(deposit.LedgerTransactionId);
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

    /// <summary>
    /// Havale için karar verilmiş. Aynı anahtarla gelen aktarım tekrarı aynı cevabı alıyor;
    /// başka her şey reddediliyor: başka bir aktarım, tamamlanan ya da süren bir iade.
    /// </summary>
    private static async Task<MoveSuspendedDepositResult> ReplayOrRejectAsync(
        WalletDbContext db, Guid depositId, SuspendedDepositResolution last, MoveSuspendedDepositCommand command,
        CancellationToken ct)
    {
        if (last.Kind is DepositResolutionKind.ReturnStarted)
        {
            throw new DepositResolutionRejectedException(
                DepositResolutionRejectedException.ReturnInProgress, "Bu havalenin iadesi sürüyor.");
        }

        if (last.Kind is DepositResolutionKind.Moved)
        {
            var moved = await db.LedgerTransactions
                .AsNoTracking()
                .Where(t => t.Id == last.LedgerTransactionId)
                .Select(t => new { t.LedgerAccountId, t.IdempotencyKey })
                .SingleAsync(ct);

            if (moved.IdempotencyKey == command.IdempotencyKey)
            {
                return new MoveSuspendedDepositResult(
                    depositId, last.AccountId!.Value, moved.LedgerAccountId, last.LedgerTransactionId, Replayed: true);
            }
        }

        throw new DepositResolutionRejectedException(
            DepositResolutionRejectedException.AlreadyResolved, "Bu havale için karar verilmiş.");
    }

    private static bool IsKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "ux_ledger_tx_idem";
}
