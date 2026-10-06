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
}
