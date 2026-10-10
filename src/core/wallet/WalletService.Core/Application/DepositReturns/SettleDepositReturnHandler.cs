using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.DepositReturns;

/// <summary>
/// İade göndericiye gitti: muhasebesi çekimdekiyle aynı kayıtla kapanıyor
/// (<see cref="BankTransferSettlement"/>): clearing −, nostro +. Ücreti platform yükleniyor;
/// göndericiye tutarın tamamı gitti. Havalenin son adımı yazılıyor ve havale listeden çıkıyor.
/// </summary>
public sealed class SettleDepositReturnHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    ProviderPolicy providers,
    IClock clock,
    ILogger<SettleDepositReturnHandler> logger)
{
    public async Task<WithdrawalReply> HandleAsync(SettleDepositReturn command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        var reply = WithdrawalReply.For(new DepositReturnSettled
        {
            SagaId = command.SagaId,
            LedgerTransactionId = transactionId
        });

        // Kapı ÖNCE: tekrar teslimde havalenin adımı çoktan "iade edildi" ve aşağıdaki
        // tutarlılık kontrolü onu hata sanırdı.
        if (!await CommandLedger.ClaimAsync(
                db, command.CommandId, nameof(SettleDepositReturn), command.SagaId, transactionId, reply, now, ct))
        {
            await transaction.RollbackAsync(ct);

            return await CommandLedger.StoredReplyAsync(contextFactory, command.CommandId, ct);
        }

        var (original, deposit, last) = await DepositReturnRecords.LoadAsync(db, command.SagaId, ct);

        if (!await SuspendedDepositSteps.AppendAsync(
                db, deposit.LedgerTransactionId, last, DepositResolutionKind.Returned, transactionId,
                accountId: null, SystemActors.DepositReturn.Id, now, ct))
        {
            throw SuspendedDepositSteps.Raced(deposit.LedgerTransactionId);
        }

        var settled = await BankTransferSettlement.RecordAsync(
            db, providers, deposit.Provider, original, command.FeeAmount, command.BankReference, transactionId,
            command.SagaId, $"{deposit.Provider}:deposit-return-settlement:{command.SagaId}", SystemActors.DepositReturn,
            now, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Havale iadesinin muhasebesi kapandı. Saga {SagaId}, havale {DepositId} → {TransactionId}: " +
            "clearing -{Owed}, nostro +{Leaving}, ücret {Fee} ({Model})",
            command.SagaId, deposit.LedgerTransactionId, transactionId, settled.Owed, settled.LeavingBank,
            command.FeeAmount, settled.Model);

        return reply;
    }
}

/// <summary>Kapanış ve geri koyma aynı kayıtlardan başlıyor: askıdan düşme, havale ve son adım.</summary>
internal static class DepositReturnRecords
{
    /// <summary>
    /// Bulunamaması iş kuralı reddi DEĞİL, tutarsızlık: orchestrator bu komutları yalnızca
    /// askıdan düşüldükten sonra üretiyor. Mesaj dead-letter'a gidip inceleniyor.
    /// </summary>
    public static async Task<(LedgerTransaction Original, SuspendedDeposit Deposit, SuspendedDepositResolution Last)> LoadAsync(
        WalletDbContext db, Guid sagaId, CancellationToken ct)
    {
        var original = await db.LedgerTransactions
                           .Include(t => t.Entries)
                           .FirstOrDefaultAsync(
                               t => t.CorrelationId == sagaId && t.Type == LedgerTransactionType.DepositReturn, ct)
                       ?? throw new InvalidOperationException($"Saga {sagaId} için askıdan düşme kaydı yok.");

        var depositId = await db.SuspendedDepositResolutions
            .Where(r => r.LedgerTransactionId == original.Id && r.Kind == DepositResolutionKind.ReturnStarted)
            .Select(r => r.SuspendedDepositId)
            .SingleAsync(ct);

        var deposit = await db.SuspendedDeposits.AsNoTracking().SingleAsync(d => d.LedgerTransactionId == depositId, ct);
        var last = await SuspendedDepositSteps.LastAsync(db, depositId, ct);

        // İadenin sonucu ancak bu iadenin başlangıcının ardına yazılabilir.
        if (last is not { Kind: DepositResolutionKind.ReturnStarted } || last.LedgerTransactionId != original.Id)
        {
            throw new InvalidOperationException(
                $"Saga {sagaId}: havale {depositId} iadenin beklediği adımda değil ({last?.Kind}).");
        }

        return (original, deposit, last);
    }
}
