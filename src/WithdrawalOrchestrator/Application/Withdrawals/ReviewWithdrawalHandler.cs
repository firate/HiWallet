using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WithdrawalOrchestrator.Application.Withdrawals;

/// <param name="Saga">Kararın ardından saga; çekim yoksa <c>null</c>.</param>
public sealed record ReviewOutcome(WithdrawalSaga? Saga, TransitionResult Result);

/// <summary>
/// İncelemedeki çekim için çalışanın kararı: serbest bırak ya da iptal et. Geçiş ve
/// üretilen komut AYNI transaction'da (decisions.md madde 32), olay tüketimindeki kalıbın
/// aynısı. Kararın geçerli olup olmadığını state machine söylüyor.
/// </summary>
public sealed class ReviewWithdrawalHandler(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    TimeProvider timeProvider)
{
    /// <summary>Banka komutu gidiyor; kararı veren çalışan saga'ya yazılıyor.</summary>
    public Task<ReviewOutcome> ReleaseAsync(Guid withdrawalId, string reviewer, CancellationToken ct) =>
        DecideAsync(withdrawalId, (saga, now) =>
        {
            var command = OutboxMessage.For(saga.Id, commandId => new StartBankTransfer
            {
                CommandId = commandId,
                SagaId = saga.Id,
                Amount = saga.Amount,
                Currency = saga.Currency,
                DestinationIban = saga.Destination.Value
            }, now);

            return (saga.Release(reviewer, command.Id, now), command);
        }, ct);

    /// <summary>
    /// Para cüzdana geri veriliyor. Ters kaydın aktörü iptal eden çalışan: iadeyi ne
    /// müşteri istedi ne banka reddetti (decisions.md madde 34). Tutar taşınmıyor;
    /// ters kayıt orijinalin aynası ve onu wallet yazdı.
    /// </summary>
    public Task<ReviewOutcome> CancelAsync(Guid withdrawalId, string reviewer, string reason, CancellationToken ct) =>
        DecideAsync(withdrawalId, (saga, now) =>
        {
            var result = saga.Cancel(reviewer, reason, now);

            if (result is not TransitionResult.Applied) return (result, null);

            var command = OutboxMessage.For(saga.Id, commandId => new RefundWithdrawal
            {
                CommandId = commandId,
                SagaId = saga.Id,
                Actor = saga.ReviewerActor()
            }, now);

            return (result, command);
        }, ct);

    private async Task<ReviewOutcome> DecideAsync(
        Guid withdrawalId,
        Func<WithdrawalSaga, DateTimeOffset, (TransitionResult Result, OutboxMessage? Command)> decide,
        CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var saga = await db.Sagas.FirstOrDefaultAsync(s => s.Id == withdrawalId, ct);

        if (saga is null)
        {
            return new ReviewOutcome(null, TransitionResult.Conflict);
        }

        var (result, command) = decide(saga, timeProvider.GetUtcNow());

        if (result is not TransitionResult.Applied)
        {
            return new ReviewOutcome(saga, result);
        }

        if (command is not null)
        {
            db.Outbox.Add(command);
        }

        // Aynı anda gelen bir olay ya da ikinci bir karar saga'nın version'ına takılıyor.
        await db.SaveChangesAsync(ct);

        return new ReviewOutcome(saga, result);
    }
}
