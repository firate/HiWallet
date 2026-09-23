using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Balances;

public sealed class GetWalletMovementsHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<WalletMovementPage> HandleAsync(
        GetWalletMovementsQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // Sistem hesabı bu endpoint'ten görünmüyor: clearing ve revenue hareketleri
        // iç muhasebe, public API'nin cevaplayacağı soru değil. Cüzdan sorgusundaki
        // kuralın aynısı.
        var exists = await db.LedgerAccounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == query.WalletId && a.Type == LedgerAccountType.UserWallet, ct);

        if (!exists)
        {
            throw new WalletNotFoundException(query.WalletId);
        }

        var size = Math.Clamp(query.Size, 1, WalletMovementPage.MaxSize);

        var rows = db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.LedgerAccountId == query.WalletId);

        if (query.After is { } after)
        {
            // Sıra azalan, yani "sonraki sayfa" daha KÜÇÜK kimlikler.
            rows = rows.Where(e => e.Id < after);
        }

        // Bir fazla okunuyor: son sayfada mıyız sorusunu ikinci bir COUNT sorgusu
        // koşmadan cevaplıyor.
        var page = await rows
            .OrderByDescending(e => e.Id)
            .Take(size + 1)
            .Join(
                db.LedgerTransactions.AsNoTracking(),
                entry => entry.TransactionId,
                transaction => transaction.Id,
                (entry, transaction) => new { entry, transaction.Type })
            .ToListAsync(ct);

        var hasMore = page.Count > size;
        var items = page.Take(size).ToArray();

        return new WalletMovementPage(
            [.. items.Select(row => new WalletMovementView(
                row.entry.Id,
                row.entry.TransactionId,
                row.Type.ToText(),
                row.entry.Amount,
                row.entry.Currency.Code,
                row.entry.FundType.ToText(),
                row.entry.CreatedAt))],
            size,
            hasMore ? items[^1].entry.Id : null);
    }
}
