using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Promos;

public sealed class GetWalletPromosHandler(IDbContextFactory<WalletDbContext> contextFactory, IClock clock)
{
    public async Task<WalletPromoPage> HandleAsync(GetWalletPromosQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var exists = await db.LedgerAccounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == query.WalletId && a.Type == LedgerAccountType.UserWallet, ct);

        if (!exists)
        {
            throw new WalletNotFoundException(query.WalletId);
        }

        var size = Math.Clamp(query.Size, 1, WalletPromoPage.MaxSize);

        var rows = db.PromoGrants
            .AsNoTracking()
            .Where(g => g.LedgerAccountId == query.WalletId);

        if (query.After is { } after)
        {
            // Cursor bu cüzdanın bir partisi değilse sonraki sayfa yok: başka bir
            // cüzdanın partisinden devam etmek o cüzdanın sırasını sızdırırdı.
            var anchor = await db.PromoGrants
                .AsNoTracking()
                .Where(g => g.Id == after && g.LedgerAccountId == query.WalletId)
                .Select(g => new { g.CreatedAt, g.Id })
                .SingleOrDefaultAsync(ct);

            if (anchor is null)
            {
                return new WalletPromoPage([], size, null);
            }

            // Sıra azalan, yani "sonraki sayfa" (created_at, id) çiftinde daha küçük olanlar.
            // Satır karşılaştırması: aynı an'da açılmış iki parti de sayfa sınırında kaybolmuyor.
            rows = rows.Where(g => EF.Functions.LessThan(
                ValueTuple.Create(g.CreatedAt, g.Id), ValueTuple.Create(anchor.CreatedAt, anchor.Id)));
        }

        // Bir fazla okunuyor: son sayfada mıyız sorusu COUNT sorgusu koşmadan cevaplanıyor.
        var page = await rows
            .OrderByDescending(g => g.CreatedAt)
            .ThenByDescending(g => g.Id)
            .Take(size + 1)
            .Select(g => new
            {
                g.Id,
                g.Amount,
                g.Currency,
                g.Funder,
                g.Scope,
                g.ExpiresAt,
                g.CreatedAt,
                Consumed = db.PromoConsumptions.Where(c => c.GrantId == g.Id).Sum(c => (decimal?)c.Amount) ?? 0m,
                Merchants = g.Merchants.Select(m => m.AccountId).ToList()
            })
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var hasMore = page.Count > size;
        var items = page.Take(size).ToArray();

        return new WalletPromoPage(
            [.. items.Select(g => new WalletPromoView(
                g.Id,
                g.Amount,
                g.Amount - g.Consumed,
                g.Currency.Code,
                g.Funder.ToText(),
                g.Scope.ToText(),
                g.Merchants,
                g.ExpiresAt,
                g.ExpiresAt <= now,
                g.CreatedAt))],
            size,
            hasMore ? items[^1].Id : null);
    }
}
