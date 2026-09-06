using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Balances;

public sealed class GetWalletHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<WalletView> HandleAsync(GetWalletQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var row = await db.LedgerAccounts
            .AsNoTracking()
            // Sistem hesabı bu uçtan görünmüyor: clearing ve revenue bakiyeleri iç
            // muhasebe, public API'nin cevaplayacağı soru değil.
            .Where(a => a.Id == query.WalletId && a.Type == LedgerAccountType.UserWallet)
            .Join(
                db.LedgerBalances.AsNoTracking(),
                wallet => wallet.Id,
                balance => balance.LedgerAccountId,
                // Currency value converter ile eşleniyor; SQL'e çevrilebilmesi için
                // tipin KENDİSİ seçiliyor, .Code'a burada inilmiyor.
                (wallet, balance) => new
                {
                    wallet.Id,
                    wallet.AccountId,
                    wallet.Name,
                    wallet.Currency,
                    balance.Balance,
                    balance.UpdatedAt
                })
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            throw new WalletNotFoundException(query.WalletId);
        }

        return new WalletView(
            row.Id, row.AccountId!.Value, row.Name!, row.Currency.Code, row.Balance, row.UpdatedAt);
    }
}
