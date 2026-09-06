using HiWallet.WalletService.Domain.Errors;
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
        // büyüyebilen liste uçlarında (işlem geçmişi) durum farklı olacak.
        var wallets = await db.LedgerAccounts
            .AsNoTracking()
            .Where(a => a.AccountId == query.AccountId)
            .Join(
                db.LedgerBalances.AsNoTracking(),
                wallet => wallet.Id,
                balance => balance.LedgerAccountId,
                (wallet, balance) => new { wallet.Id, wallet.Name, wallet.Currency, balance.Balance })
            .OrderBy(row => row.Name)
            .ToListAsync(ct);

        return new AccountView(
            account.Id,
            account.Type,
            account.CreatedAt,
            wallets
                .Select(row => new AccountWalletView(row.Id, row.Name!, row.Currency.Code, row.Balance))
                .ToArray());
    }
}
