using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WithdrawalOrchestrator.Application.DepositReturns;

/// <summary>
/// Wallet'tan ve bankadan gelen event'leri iadeye uygular ve bir sonraki komutu AYNI
/// transaction'da outbox'a koyar (decisions.md madde 32). Kural durum makinesinde; burası
/// sonucu kalıcı yapıyor ve <c>Conflict</c>'i görünür kılıyor
/// (<see cref="AdvanceSagaHandler"/> ile aynı).
/// </summary>
public sealed class AdvanceDepositReturnHandler(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<AdvanceDepositReturnHandler> logger)
{
    public Task<TransitionResult> HandleAsync(SuspenseDebitedForReturn @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(SuspenseDebitedForReturn), (saga, now) =>
        {
            // Bankaya giden komut düşmeyle aynı geçişte: kimliği saga'ya yazılıyor. IBAN
            // yok; adaptör havaleyi bankanın referansıyla bulup IBAN'ı kendisi okuyor.
            var command = OutboxMessage.For(saga.Id, commandId => new ReturnBankDeposit
            {
                CommandId = commandId,
                SagaId = saga.Id,
                Provider = @event.Provider,
                DepositBankReference = @event.DepositBankReference,
                Amount = @event.Amount,
                Currency = @event.Currency
            }, now);

            var result = saga.Debited(
                @event.LedgerTransactionId, @event.Amount, @event.Currency, @event.Provider,
                @event.DepositBankReference, command.Id, now);

            return (result, result is TransitionResult.Applied ? command : null);
        }, ct);

    public Task<TransitionResult> HandleAsync(SuspenseDebitForReturnRejected @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(SuspenseDebitForReturnRejected), (saga, now) =>
            // Para hareket etmedi; iade burada bitiyor.
            (saga.Rejected(@event.Reason, @event.Rule, now), null), ct);

    public Task<TransitionResult> HandleAsync(BankTransferSucceeded @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(BankTransferSucceeded), (saga, now) =>
        {
            var result = saga.BankTransferSucceeded(@event.BankReference, @event.FeeAmount, now);

            if (result is not TransitionResult.Applied) return (result, null);

            // Ücret taşınıyor, tutar taşınmıyor: tutarı wallet yazdı, ücreti banka biliyor.
            var command = OutboxMessage.For(saga.Id, commandId => new SettleDepositReturn
            {
                CommandId = commandId,
                SagaId = saga.Id,
                FeeAmount = @event.FeeAmount,
                BankReference = @event.BankReference
            }, now);

            return (result, command);
        }, ct);

    public Task<TransitionResult> HandleAsync(DepositReturnSettled @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(DepositReturnSettled), (saga, now) =>
            (saga.Settled(@event.LedgerTransactionId, now), null), ct);

    public Task<TransitionResult> HandleAsync(BankTransferFailed @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(BankTransferFailed), (saga, now) =>
        {
            var result = saga.BankTransferFailed(@event.Reason, now);

            if (result is not TransitionResult.Applied) return (result, null);

            // Tutar taşınmıyor: ters kayıt askıdan düşmenin aynası ve onu wallet yazdı.
            var command = OutboxMessage.For(saga.Id, commandId => new RestoreSuspendedDeposit
            {
                CommandId = commandId,
                SagaId = saga.Id
            }, now);

            return (result, command);
        }, ct);

    public Task<TransitionResult> HandleAsync(SuspendedDepositRestored @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(SuspendedDepositRestored), (saga, now) =>
            (saga.Restored(@event.LedgerTransactionId, now), null), ct);

    private async Task<TransitionResult> ApplyAsync(
        Guid sagaId,
        string eventName,
        Func<DepositReturnSaga, DateTimeOffset, (TransitionResult Result, OutboxMessage? Command)> step,
        CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var saga = await db.DepositReturns.FirstOrDefaultAsync(s => s.Id == sagaId, ct)
                   ?? throw new SagaNotFoundException(sagaId);

        var (result, command) = step(saga, timeProvider.GetUtcNow());

        switch (result)
        {
            case TransitionResult.Ignored:
                logger.LogDebug(
                    "{Event} yok sayıldı, iade zaten {State}. Saga {SagaId}", eventName, saga.State, sagaId);
                return result;

            case TransitionResult.Conflict:
                // Alarm: para hem göndericiye gitmiş hem askıya dönmüş olabilir (madde 31).
                logger.LogError(
                    "{Event} iade durumuyla ÇELİŞİYOR. Saga {SagaId} durumu {State}, işlem yapılmadı.",
                    eventName, sagaId, saga.State);
                return result;
        }

        if (command is not null)
        {
            db.Outbox.Add(command);
        }

        await db.SaveChangesAsync(ct);

        return result;
    }
}

/// <summary>
/// Bankanın sonucu iki saga'ya da aynı event'lerle geliyor; hangisine ait olduğunu saga
/// kimliği söylüyor. Önce iadeye bakılıyor, yoksa çekim: bilinmeyen kimlik çekimin
/// handler'ında dead-letter'a gidiyor.
/// </summary>
public sealed class BankTransferResults(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    AdvanceSagaHandler withdrawals,
    AdvanceDepositReturnHandler returns)
{
    public Task<TransitionResult> HandleAsync(object @event, CancellationToken ct) => @event switch
    {
        BankTransferSucceeded succeeded => RouteAsync(
            succeeded.SagaId, () => returns.HandleAsync(succeeded, ct), () => withdrawals.HandleAsync(succeeded, ct), ct),
        BankTransferFailed failed => RouteAsync(
            failed.SagaId, () => returns.HandleAsync(failed, ct), () => withdrawals.HandleAsync(failed, ct), ct),
        _ => throw new ArgumentException($"Bankanın sonucu değil: {@event.GetType().Name}", nameof(@event))
    };

    private async Task<TransitionResult> RouteAsync(
        Guid sagaId, Func<Task<TransitionResult>> deposit, Func<Task<TransitionResult>> withdrawal, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        return await db.DepositReturns.AnyAsync(s => s.Id == sagaId, ct) ? await deposit() : await withdrawal();
    }
}
