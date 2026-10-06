using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class ListAccountsHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<AccountPage> HandleAsync(ListAccountsQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var size = Math.Clamp(query.Size, 1, AccountPage.MaxSize);

        var rows = db.AccountMembers
            .AsNoTracking()
            .Where(m => m.Subject == query.Subject)
            .Join(db.Accounts.AsNoTracking(), m => m.AccountId, a => a.Id, (_, a) => a);

        if (query.After is { } after)
        {
            // Cursor bu kimliğin hesabı değilse sonraki sayfa yok: başka bir kimliğin
            // hesabından devam etmek onun hesabının varlığını sızdırırdı.
            var anchor = await rows
                .Where(a => a.Id == after)
                .Select(a => new { a.CreatedAt, a.Id })
                .SingleOrDefaultAsync(ct);

            if (anchor is null)
            {
                return new AccountPage([], size, null);
            }

            rows = rows.Where(a => EF.Functions.LessThan(
                ValueTuple.Create(a.CreatedAt, a.Id), ValueTuple.Create(anchor.CreatedAt, anchor.Id)));
        }

        // Bir fazla okunuyor: son sayfada mıyız sorusu COUNT sorgusu koşmadan cevaplanıyor.
        var page = await rows
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(size + 1)
            .Select(a => new AccountSummary(a.Id, a.Number, a.Type, a.KycLevel, a.CreatedAt))
            .ToListAsync(ct);

        var hasMore = page.Count > size;
        var items = hasMore ? page[..size] : page;

        return new AccountPage(items, size, hasMore ? items[^1].AccountId : null);
    }
}
