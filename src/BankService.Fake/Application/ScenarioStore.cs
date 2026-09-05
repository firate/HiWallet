using HiWallet.BankService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.BankService.Application;

/// <summary>
/// Senaryoların kurulduğu ve okunduğu yer. Test bir saga için "banka reddedecek"
/// dediğinde bu tabloya yazılıyor (overview.md madde 9).
/// </summary>
public sealed class ScenarioStore(
    IDbContextFactory<BankDbContext> contextFactory, TimeProvider timeProvider)
{
    /// <summary>
    /// Senaryoyu kurar ya da mevcut olanı DEĞİŞTİRİR. Aynı saga için ikinci kez
    /// çağrılırsa üzerine yazıyor; test bir senaryoyu kurup sonra fikrini
    /// değiştirebilmeli ve bunun için kaydı silmek zorunda kalmamalı.
    /// </summary>
    public async Task ArmAsync(
        Guid sagaId, TransferOutcome outcome, int transientFailures, int delayMilliseconds,
        CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var existing = await db.Scenarios.FirstOrDefaultAsync(s => s.SagaId == sagaId, ct);

        if (existing is not null)
        {
            db.Scenarios.Remove(existing);
        }

        db.Scenarios.Add(new TransferScenario
        {
            SagaId = sagaId,
            Outcome = outcome.ToString(),
            RemainingTransientFailures =
                outcome is TransferOutcome.TransientFailure ? transientFailures : 0,
            DelayMilliseconds = delayMilliseconds,
            Attempts = 0,
            CreatedAt = timeProvider.GetUtcNow()
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Senaryonun o anki hali. Testler "kaç denemede sonuçlandı" sorusunu buradan
    /// cevaplıyor.
    /// </summary>
    public async Task<ScenarioState?> FindAsync(Guid sagaId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var scenario = await db.Scenarios
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SagaId == sagaId, ct);

        return scenario is null
            ? null
            : new ScenarioState(
                scenario.SagaId,
                scenario.Outcome,
                scenario.RemainingTransientFailures,
                scenario.Attempts);
    }
}

public sealed record ScenarioState(
    Guid SagaId, string Outcome, int RemainingTransientFailures, int Attempts);
