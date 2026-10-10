using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Deposits;

/// <summary>
/// Askıdaki havaleler, panel için; karar verilmiş olanlar dışarıda. Gönderenin kişisel verisi (adı, IBAN'ı, kimlik
/// numarası) wallet'ta yok; liste banka referansını, tutarı, sebebi ve açıklamadaki
/// numaranın hesabını veriyor.
/// </summary>
public sealed class ListSuspendedDepositsHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<SuspendedDepositPage> HandleAsync(ListSuspendedDepositsQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var size = Math.Clamp(query.Size, 1, SuspendedDepositPage.MaxSize);
        var all = db.SuspendedDeposits.AsNoTracking();

        // Karar verilmiş havale askıda değil: listede yok.
        var rows = all.Where(d => !db.SuspendedDepositResolutions.Any(r => r.SuspendedDepositId == d.LedgerTransactionId));

        if (query.After is { } after)
        {
            // İmleç bütün satırlarda aranıyor: sayfalar arasında karar verilen havale
            // sonraki sayfayı boşaltmasın.
            var anchor = await all
                .Where(d => d.LedgerTransactionId == after)
                .Select(d => new { d.CreatedAt, d.LedgerTransactionId })
                .SingleOrDefaultAsync(ct);

            if (anchor is null)
            {
                return new SuspendedDepositPage([], size, null);
            }

            rows = rows.Where(d => EF.Functions.LessThan(
                ValueTuple.Create(d.CreatedAt, d.LedgerTransactionId),
                ValueTuple.Create(anchor.CreatedAt, anchor.LedgerTransactionId)));
        }

        // Bir fazla okunuyor: son sayfada mıyız sorusu COUNT sorgusu koşmadan cevaplanıyor.
        var page = await rows
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.LedgerTransactionId)
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > size;
        var rowsOnPage = hasMore ? page[..size] : page;

        var accountIds = rowsOnPage.Where(d => d.AccountId is not null).Select(d => d.AccountId!.Value).Distinct().ToList();
        var numbers = await db.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Number, ct);

        var items = rowsOnPage
            .Select(d => new SuspendedDepositView(
                d.LedgerTransactionId, d.Provider, d.BankReference, d.Money, d.Reason, d.AccountId,
                d.AccountId is { } id && numbers.TryGetValue(id, out var number) ? number : null,
                d.ReceivedAt, d.CreatedAt))
            .ToList();

        return new SuspendedDepositPage(items, size, hasMore ? items[^1].Id : null);
    }
}
