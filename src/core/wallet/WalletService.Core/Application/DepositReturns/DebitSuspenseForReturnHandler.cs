using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.DepositReturns;

/// <summary>
/// Askıdaki havaleyi iade için askıdan düşer: askı −, havaleyi alan bankanın clearing'i +.
/// Aktör iadeyi isteyen çalışan (komuttan). Ledger tipi
/// <see cref="LedgerTransactionType.DepositReturn"/>, korelasyon iade saga'sı.
///
/// Aktarımla aynı kapıdan geçiyor (<see cref="SuspendedDepositSteps"/>): aktarılmış, iade
/// edilmiş ya da iadesi süren havale düşülmüyor ve saga'ya ret cevabı dönüyor. Ret de
/// deftere yazılıyor: tekrar teslimde aynı cevap gitsin.
///
/// Cevap bankaya gidecek komutun ihtiyacı olan her şeyi taşıyor: tutar, para birimi, banka
/// ve havalenin bankadaki referansı. Orchestrator havaleyi bilmiyor.
/// </summary>
public sealed class DebitSuspenseForReturnHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock,
    ILogger<DebitSuspenseForReturnHandler> logger)
{
    /// <summary>Aynı bankanın askı satırını havaleler de güncelliyor; çakışmada taze okumayla yeniden.</summary>
    private const int MaxAttempts = 3;

    /// <summary>Ledger anahtarı; kapsam askı hesabı. Bir saga bir kez düşer.</summary>
    public static string IdempotencyKey(Guid sagaId) => $"deposit-return:{sagaId}";

    public async Task<WithdrawalReply> HandleAsync(DebitSuspenseForReturn command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DebitAsync(command, ct);
            }
            catch (DepositResolutionRejectedException rejection)
            {
                logger.LogInformation(
                    "Havale iadesi reddedildi. Saga {SagaId}, havale {DepositId}: {Rule}",
                    command.SagaId, command.SuspendedDepositId, rejection.Rule);

                return await RecordRejectionAsync(command, rejection, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogDebug(
                    "Askıdan düşme çakıştı, yeniden deneniyor. Deneme {Attempt}/{Max}, saga {SagaId}",
                    attempt, MaxAttempts, command.SagaId);
            }
        }
    }

    private async Task<WithdrawalReply> DebitAsync(DebitSuspenseForReturn command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var deposit = await db.SuspendedDeposits
                          .AsNoTracking()
                          .SingleOrDefaultAsync(d => d.LedgerTransactionId == command.SuspendedDepositId, ct)
                      ?? throw new DepositResolutionRejectedException(
                          DepositResolutionRejectedException.NotFound, "Askıdaki havale bulunamadı.");

        // Kapı. Tekrar teslim de buraya takılıyor (iadesi sürüyor) ve ret yolunda defterin
        // kapısı saklanan cevabı dönüyor.
        var last = await SuspendedDepositSteps.LastAsync(db, deposit.LedgerTransactionId, ct);

        if (!SuspendedDepositResolution.IsOpenAfter(last?.Kind))
        {
            throw last!.Kind is DepositResolutionKind.ReturnStarted
                ? new DepositResolutionRejectedException(
                    DepositResolutionRejectedException.ReturnInProgress, "Bu havalenin iadesi sürüyor.")
                : new DepositResolutionRejectedException(
                    DepositResolutionRejectedException.AlreadyResolved, "Bu havale için karar verilmiş.");
        }

        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();
        var actor = Actor.From(command.Actor);

        if (!await SuspendedDepositSteps.AppendAsync(
                db, deposit.LedgerTransactionId, last, DepositResolutionKind.ReturnStarted, transactionId,
                accountId: null, actor.Id, now, ct))
        {
            throw SuspendedDepositSteps.Raced(deposit.LedgerTransactionId);
        }

        var amount = deposit.Money;
        var suspense = await SystemAccountAsync(db, deposit.Provider, LedgerAccountType.Suspense, amount.Currency, ct);
        var clearing = await SystemAccountAsync(db, deposit.Provider, LedgerAccountType.Clearing, amount.Currency, ct);

        var tx = LedgerTransaction
            .Create(transactionId, LedgerTransactionType.DepositReturn, suspense.Id, actor, now,
                IdempotencyKey(command.SagaId), command.SagaId)
            .AddEntry(suspense.Id, amount.Negated, FundType.Cash)
            .AddEntry(clearing.Id, amount, FundType.Cash);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8). Askı negatife
        // düşemiyor; clearing sistem hesabı, düşebiliyor.
        foreach (var (entry, account) in tx.Entries
                     .Select(e => (e, e.LedgerAccountId == suspense.Id ? suspense : clearing))
                     .OrderBy(x => x.e.LedgerAccountId))
        {
            var balance = await db.LedgerBalances.SingleAsync(
                b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct);

            balance.Apply(entry.Money, account.CanGoNegative, now);
        }

        var reply = WithdrawalReply.For(new SuspenseDebitedForReturn
        {
            SagaId = command.SagaId,
            LedgerTransactionId = transactionId,
            Amount = amount.Amount,
            Currency = amount.Currency.Code,
            Provider = deposit.Provider,
            DepositBankReference = deposit.BankReference
        });

        if (!await CommandLedger.ClaimAsync(
                db, command.CommandId, nameof(DebitSuspenseForReturn), command.SagaId, transactionId, reply, now, ct))
        {
            await transaction.RollbackAsync(ct);

            return await CommandLedger.StoredReplyAsync(contextFactory, command.CommandId, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Havale iade için askıdan düşüldü. Saga {SagaId}, havale {DepositId} → {TransactionId}, {Amount} {Currency}",
            command.SagaId, deposit.LedgerTransactionId, transactionId, amount.Amount, amount.Currency.Code);

        return reply;
    }

    /// <summary>
    /// Reddi de deftere yazar: tekrar teslimde karar yeniden değerlendirilmesin. Komut daha
    /// önce başarıyla işlendiyse kapı saklanan o cevabı dönüyor.
    /// </summary>
    private async Task<WithdrawalReply> RecordRejectionAsync(
        DebitSuspenseForReturn command, DepositResolutionRejectedException rejection, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var reply = WithdrawalReply.For(new SuspenseDebitForReturnRejected
        {
            SagaId = command.SagaId,
            Reason = rejection.Message,
            Rule = rejection.Rule
        });

        var claimed = await CommandLedger.ClaimAsync(
            db, command.CommandId, nameof(DebitSuspenseForReturn), command.SagaId, ledgerTransactionId: null, reply,
            clock.UtcNow, ct);

        return claimed ? reply : await CommandLedger.StoredReplyAsync(contextFactory, command.CommandId, ct);
    }

    private static async Task<LedgerAccount> SystemAccountAsync(
        WalletDbContext db, string provider, LedgerAccountType type, Currency currency, CancellationToken ct) =>
        await db.LedgerAccounts.AsNoTracking().FirstOrDefaultAsync(
            a => a.Type == type && a.Provider == provider && a.Currency == currency, ct)
        ?? throw new InvalidOperationException($"'{provider}' sağlayıcısının {currency} {type} hesabı yok.");
}
