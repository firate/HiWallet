using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.DepositReturns;

/// <summary>
/// Banka iadeyi reddetti: para askıya geri konuyor. Ters kayıt askıdan düşmenin aynası,
/// bacaklar orijinalden okunuyor (çekimin iadesindeki kural). Aktör iade akışı: bu adımı
/// çalışan istemedi, bankanın reddi tetikledi. Havale yeniden karara açık.
/// </summary>
public sealed class RestoreSuspendedDepositHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock,
    ILogger<RestoreSuspendedDepositHandler> logger)
{
    public static string IdempotencyKey(Guid sagaId) => $"deposit-return-restore:{sagaId}";

    public async Task<WithdrawalReply> HandleAsync(RestoreSuspendedDeposit command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        var reply = WithdrawalReply.For(new SuspendedDepositRestored
        {
            SagaId = command.SagaId,
            LedgerTransactionId = transactionId
        });

        // Kapı ÖNCE; kapanıştaki gerekçe.
        if (!await CommandLedger.ClaimAsync(
                db, command.CommandId, nameof(RestoreSuspendedDeposit), command.SagaId, transactionId, reply, now, ct))
        {
            await transaction.RollbackAsync(ct);

            return await CommandLedger.StoredReplyAsync(contextFactory, command.CommandId, ct);
        }

        var (original, deposit, last) = await DepositReturnRecords.LoadAsync(db, command.SagaId, ct);

        if (!await SuspendedDepositSteps.AppendAsync(
                db, deposit.LedgerTransactionId, last, DepositResolutionKind.ReturnFailed, transactionId,
                accountId: null, SystemActors.DepositReturn.Id, now, ct))
        {
            throw SuspendedDepositSteps.Raced(deposit.LedgerTransactionId);
        }

        var tx = LedgerTransaction.Create(
            transactionId, LedgerTransactionType.Refund, original.LedgerAccountId, SystemActors.DepositReturn, now,
            IdempotencyKey(command.SagaId), command.SagaId);

        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8); kova orijinalden.
        foreach (var entry in original.Entries.OrderBy(e => e.LedgerAccountId))
        {
            var account = await db.LedgerAccounts.AsNoTracking().SingleAsync(a => a.Id == entry.LedgerAccountId, ct);
            var balance = await db.LedgerBalances.SingleAsync(
                b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct);

            balance.Apply(entry.Money.Negated, account.CanGoNegative, now);
            tx.AddEntry(entry.LedgerAccountId, entry.Money.Negated, entry.FundType);
        }

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Havale iadesi gitmedi, askıya geri kondu. Saga {SagaId}, havale {DepositId} → {TransactionId}",
            command.SagaId, deposit.LedgerTransactionId, transactionId);

        return reply;
    }
}
