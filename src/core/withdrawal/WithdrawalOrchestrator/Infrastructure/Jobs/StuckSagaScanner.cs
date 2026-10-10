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

/// <param name="Kind"><c>withdrawal</c> ya da <c>deposit_return</c>.</param>
/// <param name="State">Durumun dış dünyadaki adı.</param>
internal sealed record StuckSaga(Guid SagaId, string Kind, string State, DateTimeOffset UpdatedAt);

/// <summary>
/// Taramanın kendisi. Zamanlamadan ayrı duruyor ki test 5 dakika beklemek zorunda
/// kalmasın ve log metnine değil veriye baksın.
///
/// Çekim ve askıdaki havalenin iadesi birlikte taranıyor. Durum listeleri
/// <see cref="WithdrawalStates.AwaitingSystem"/> ve <see cref="DepositReturnStates.Active"/>'ten
/// türüyor, elle yazılmıyor:
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
        var withdrawalStates = WithdrawalStates.AwaitingSystem.ToArray();
        var returnStates = DepositReturnStates.Active.ToArray();

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var withdrawals = db.Sagas
            .AsNoTracking()
            .Where(saga => withdrawalStates.Contains(saga.State) && saga.UpdatedAt < cutoff);

        var returns = db.DepositReturns
            .AsNoTracking()
            .Where(saga => returnStates.Contains(saga.State) && saga.UpdatedAt < cutoff);

        var total = await withdrawals.CountAsync(ct) + await returns.CountAsync(ct);

        if (total == 0)
        {
            return new StuckSagaReport(0, []);
        }

        // İki tablo ayrı okunup birleştiriliyor; her biri en çok örneklem kadar.
        var oldestWithdrawals = await withdrawals
            .OrderBy(saga => saga.UpdatedAt)
            .Take(_options.SampleSize)
            .Select(saga => new { saga.Id, saga.State, saga.UpdatedAt })
            .ToListAsync(ct);

        var oldestReturns = await returns
            .OrderBy(saga => saga.UpdatedAt)
            .Take(_options.SampleSize)
            .Select(saga => new { saga.Id, saga.State, saga.UpdatedAt })
            .ToListAsync(ct);

        var oldest = oldestWithdrawals
            .Select(saga => new StuckSaga(saga.Id, "withdrawal", saga.State.ToText(), saga.UpdatedAt))
            .Concat(oldestReturns.Select(saga =>
                new StuckSaga(saga.Id, "deposit_return", saga.State.ToText(), saga.UpdatedAt)))
            .OrderBy(saga => saga.UpdatedAt)
            .Take(_options.SampleSize)
            .ToList();

        return new StuckSagaReport(total, oldest);
    }
}
