using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class GetAccountHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<AccountView> HandleAsync(GetAccountQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == query.AccountId, ct)
            ?? throw new AccountNotFoundException(query.AccountId);

        // Cüzdan sayısı hesap başına küçük; pagination gerekmiyor. Sınırsız
        // büyüyebilen liste endpoint'lerinde (işlem geçmişi) durum farklı olacak.
        // Cüzdan başına kova sayısı kadar satır geliyor (decisions.md madde 36);
        // join'in sonucu cüzdan başına GRUPLANIYOR. Gruplanmasaydı aynı cüzdan
        // listede üç kez görünürdü.
        var rows = await db.LedgerAccounts
            .AsNoTracking()
            .Where(a => a.AccountId == query.AccountId)
            .Join(
                db.LedgerBalances.AsNoTracking(),
                wallet => wallet.Id,
                balance => balance.LedgerAccountId,
                (wallet, balance) => new
                {
                    wallet.Id,
                    wallet.Name,
                    wallet.Currency,
                    balance.FundType,
                    balance.Balance
                })
            .ToListAsync(ct);

        var wallets = rows
            .GroupBy(row => new { row.Id, row.Name, row.Currency })
            .OrderBy(group => group.Key.Name)
            .Select(group => new AccountWalletView(
                group.Key.Id,
                group.Key.Name!,
                group.Key.Currency.Code,
                group.Sum(row => row.Balance),
                group.Where(row => row.FundType.CanWithdraw()).Sum(row => row.Balance),
                FundTypes.All
                    .Select(fundType => new WalletBalanceView(
                        fundType.ToText(),
                        group.Where(row => row.FundType == fundType).Sum(row => row.Balance)))
                    .ToArray()))
            .ToArray();

        return new AccountView(account.Id, account.Type, account.CreatedAt, wallets);
    }
}
