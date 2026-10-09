using HiWallet.CardTopup.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.CardTopup.Application;

public sealed class CardTopupQueries(IDbContextFactory<CardTopupDbContext> contextFactory)
{
    public async Task<Domain.CardTopup?> FindAsync(Guid cardTopupId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        return await db.CardTopups.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardTopupId, ct);
    }

    /// <summary>
    /// Bir cüzdanın kartla yüklemeleri, yeniden eskiye. Sayfalama cursor ile, açılış zamanı ve
    /// kimlik.
    /// </summary>
    /// <param name="subject">
    /// Verilirse yalnızca bu kimliğin başlattıkları. Servis hesabın kullanıcılarını bilmiyor;
    /// müşteri ancak kendi başlattığı yüklemeyi görebiliyor.
    /// </param>
    /// <param name="after">Önceki sayfanın son yüklemesinin kimliği.</param>
    public async Task<CardTopupPage> ListForWalletAsync(
        Guid walletId, string? subject, Guid? after, int size, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        size = Math.Clamp(size, 1, CardTopupPage.MaxSize);
        var rows = db.CardTopups.AsNoTracking().Where(c => c.WalletId == walletId);

        if (subject is not null)
        {
            rows = rows.Where(c => c.Subject == subject);
        }

        if (after is { } cursor)
        {
            var anchor = await rows
                .Where(c => c.Id == cursor)
                .Select(c => new { c.CreatedAt, c.Id })
                .SingleOrDefaultAsync(ct);

            if (anchor is null)
            {
                return new CardTopupPage([], size, null);
            }

            rows = rows.Where(c => EF.Functions.LessThan(
                ValueTuple.Create(c.CreatedAt, c.Id), ValueTuple.Create(anchor.CreatedAt, anchor.Id)));
        }

        var page = await rows
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > size;
        var items = hasMore ? page[..size] : page;

        return new CardTopupPage(items, size, hasMore ? items[^1].Id : null);
    }
}

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record CardTopupPage(IReadOnlyList<Domain.CardTopup> Items, int Size, Guid? NextCursor)
{
    public const int MaxSize = 100;

    public const int DefaultSize = 20;
}
