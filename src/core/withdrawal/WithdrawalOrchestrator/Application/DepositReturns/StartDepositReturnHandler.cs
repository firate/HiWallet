using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.WithdrawalOrchestrator.Application.DepositReturns;

/// <param name="RequestedBy">İsteyen çalışanın <c>sub</c>'ı.</param>
public sealed record StartDepositReturnCommand(Guid SuspendedDepositId, string RequestedBy, string IdempotencyKey);

public sealed record StartDepositReturnResult(Guid DepositReturnId, DepositReturnState State, bool Replayed);

/// <summary>
/// İadeyi başlatır ve ilk komutu (<see cref="DebitSuspenseForReturn"/>) aynı commit'te
/// outbox'a koyar (decisions.md madde 32). Havalenin var olup olmadığını, tutarını ve
/// karara açık olup olmadığını wallet biliyor; orchestrator yalnızca niyeti kalıcı yapıyor.
/// </summary>
public sealed class StartDepositReturnHandler(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<StartDepositReturnHandler> logger)
{
    public async Task<StartDepositReturnResult> HandleAsync(StartDepositReturnCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var saga = DepositReturnSaga.Start(
            Guid.NewGuid(), command.SuspendedDepositId, command.RequestedBy, command.IdempotencyKey, now);

        var message = OutboxMessage.For(saga.Id, commandId => new DebitSuspenseForReturn
        {
            CommandId = commandId,
            SagaId = saga.Id,
            SuspendedDepositId = saga.SuspendedDepositId,
            Actor = saga.RequesterActor()
        }, now);

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        db.DepositReturns.Add(saga);
        db.Outbox.Add(message);

        try
        {
            await db.SaveChangesAsync(ct);

            return new StartDepositReturnResult(saga.Id, saga.State, Replayed: false);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            // Tekilliğe veritabanı karar veriyor; outbox satırı da geri alındı.
            logger.LogInformation(
                "İade isteği tekrar; mevcut saga dönülüyor. Havale {DepositId}, anahtar {Key}",
                command.SuspendedDepositId, command.IdempotencyKey);
        }

        await using var read = await contextFactory.CreateDbContextAsync(ct);

        var existing = await read.DepositReturns
            .AsNoTracking()
            .SingleAsync(
                s => s.SuspendedDepositId == command.SuspendedDepositId && s.IdempotencyKey == command.IdempotencyKey,
                ct);

        return new StartDepositReturnResult(existing.Id, existing.State, Replayed: true);
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "ux_deposit_return_sagas_idempotency";
}
