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

        var rows = await db.LedgerAccounts
            .AsNoTracking()
            // Sistem hesabı bu endpoint'ten görünmüyor: clearing ve revenue bakiyeleri iç
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
                    balance.FundType,
                    balance.Balance,
                    balance.UpdatedAt
                })
            // Kova başına bir satır geliyor (decisions.md madde 36); hepsi okunup
            // toplanıyor. Tek satır alınsaydı rastgele bir kovanın bakiyesi
            // cüzdanın bakiyesi gibi dönerdi.
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            throw new WalletNotFoundException(query.WalletId);
        }

        var first = rows[0];

        return new WalletView(
            first.Id,
            first.AccountId!.Value,
            first.Name!,
            first.Currency.Code,
            rows.Sum(r => r.Balance),
            rows.Where(r => r.FundType.CanWithdraw()).Sum(r => r.Balance),
            FundTypes.All
                .Select(fundType => new WalletFundBalanceView(
                    fundType.ToText(),
                    rows.Where(r => r.FundType == fundType).Sum(r => r.Balance)))
                .ToArray(),
            rows.Max(r => r.UpdatedAt));
    }
}
