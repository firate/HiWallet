using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WithdrawalOrchestrator.Application.Withdrawals;

/// <summary>
/// Saga'nın okunması. Ayrı sınıf çünkü yazma yolundan farklı: burada değişiklik
/// izlemeye gerek yok ve saga hiç yüklenmeden de cevap verilebiliyor.
/// </summary>
public sealed class WithdrawalQueries(IDbContextFactory<OrchestratorDbContext> contextFactory)
{
    public async Task<WithdrawalSaga?> FindAsync(Guid withdrawalId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        return await db.Sagas
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == withdrawalId, ct);
    }

    /// <summary>
    /// Bir durumdaki çekimler, en eski önce: inceleme kuyruğu sırayla eritiliyor.
    /// Sayfalama cursor ile, açılış zamanı ve kimlik.
    /// </summary>
    /// <param name="after">Önceki sayfanın son çekiminin kimliği.</param>
    public async Task<WithdrawalPage> ListAsync(WithdrawalState state, Guid? after, int size, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        size = Math.Clamp(size, 1, WithdrawalPage.MaxSize);
        var rows = db.Sagas.AsNoTracking().Where(s => s.State == state);

        if (after is { } cursor)
        {
            var anchor = await rows
                .Where(s => s.Id == cursor)
                .Select(s => new { s.CreatedAt, s.Id })
                .SingleOrDefaultAsync(ct);

            if (anchor is null)
            {
                return new WithdrawalPage([], size, null);
            }

            rows = rows.Where(s => EF.Functions.GreaterThan(
                ValueTuple.Create(s.CreatedAt, s.Id), ValueTuple.Create(anchor.CreatedAt, anchor.Id)));
        }

        var page = await rows
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > size;
        var items = hasMore ? page[..size] : page;

        return new WithdrawalPage(items, size, hasMore ? items[^1].Id : null);
    }

    /// <summary>
    /// Bir cüzdanın çekimleri, yeniden eskiye. Sayfalama cursor ile, açılış zamanı ve kimlik.
    /// </summary>
    /// <param name="initiatedBy">
    /// Verilirse yalnızca bu kimliğin başlattıkları. Orchestrator hesabın kullanıcılarını
    /// bilmiyor; müşteri ancak kendi başlattığı çekimi görebiliyor.
    /// </param>
    /// <param name="after">Önceki sayfanın son çekiminin kimliği.</param>
    public async Task<WithdrawalPage> ListForWalletAsync(
        Guid walletId, string? initiatedBy, Guid? after, int size, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        size = Math.Clamp(size, 1, WithdrawalPage.MaxSize);
        var rows = db.Sagas.AsNoTracking().Where(s => s.WalletId == walletId);

        if (initiatedBy is not null)
        {
            rows = rows.Where(s => s.InitiatedBySubject == initiatedBy);
        }

        if (after is { } cursor)
        {
            var anchor = await rows
                .Where(s => s.Id == cursor)
                .Select(s => new { s.CreatedAt, s.Id })
                .SingleOrDefaultAsync(ct);

            if (anchor is null)
            {
                return new WithdrawalPage([], size, null);
            }

            rows = rows.Where(s => EF.Functions.LessThan(
                ValueTuple.Create(s.CreatedAt, s.Id), ValueTuple.Create(anchor.CreatedAt, anchor.Id)));
        }

        var page = await rows
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > size;
        var items = hasMore ? page[..size] : page;

        return new WithdrawalPage(items, size, hasMore ? items[^1].Id : null);
    }
}

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record WithdrawalPage(IReadOnlyList<WithdrawalSaga> Items, int Size, Guid? NextCursor)
{
    public const int MaxSize = 100;

    public const int DefaultSize = 20;
}
