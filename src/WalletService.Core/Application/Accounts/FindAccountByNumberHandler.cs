using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class FindAccountByNumberHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<Guid> HandleAsync(FindAccountByNumberQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var ids = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Number == query.Number)
            .Select(a => a.Id)
            .ToListAsync(ct);

        return ids.Count == 1 ? ids[0] : throw new AccountNumberNotFoundException(query.Number);
    }
}
