using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Jobs;

/// <param name="Total">
/// Eşiği aşan saga sayısının TAMAMI. <paramref name="Oldest"/> örneklemle sınırlı,
/// bu değil — alarm "20'den fazla" demek yerine kaç tane olduğunu söylemeli.
/// </param>
internal sealed record StuckSagaReport(int Total, IReadOnlyList<StuckSaga> Oldest)
{
    public bool IsEmpty => Total == 0;
}

internal sealed record StuckSaga(Guid SagaId, WithdrawalState State, DateTimeOffset UpdatedAt);

/// <summary>
/// Taramanın kendisi. Zamanlamadan ayrı duruyor ki test 5 dakika beklemek zorunda
/// kalmasın ve log metnine değil veriye baksın.
///
/// Durum listesi <see cref="WithdrawalStates.Active"/>'ten türüyor, elle yazılmıyor:
/// yeni bir terminal durum eklendiğinde tarama onu "takılmış" diye raporlamaya
/// başlardı.
/// </summary>
internal sealed class StuckSagaScanner(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    TimeProvider timeProvider,
    IOptions<StuckSagaScanOptions> options)
{
    private readonly StuckSagaScanOptions _options = options.Value;

    public TimeSpan Threshold => _options.Threshold;

    public async Task<StuckSagaReport> ScanAsync(CancellationToken ct)
    {
        var cutoff = timeProvider.GetUtcNow() - _options.Threshold;
        var active = WithdrawalStates.Active.ToArray();

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var query = db.Sagas
            .AsNoTracking()
            .Where(saga => active.Contains(saga.State) && saga.UpdatedAt < cutoff);

        var total = await query.CountAsync(ct);

        if (total == 0)
        {
            return new StuckSagaReport(0, []);
        }

        var oldest = await query
            .OrderBy(saga => saga.UpdatedAt)
            .Select(saga => new StuckSaga(saga.Id, saga.State, saga.UpdatedAt))
            .Take(_options.SampleSize)
            .ToListAsync(ct);

        return new StuckSagaReport(total, oldest);
    }
}
