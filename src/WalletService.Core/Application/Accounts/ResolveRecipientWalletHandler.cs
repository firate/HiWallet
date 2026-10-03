using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class ResolveRecipientWalletHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<Guid> HandleAsync(ResolveRecipientWalletQuery query, CancellationToken ct)
    {
        var currency = Currency.From(query.Currency);

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var recipient = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Number == query.Number)
            .Select(a => new
            {
                Wallets = db.DefaultWallets
                    .Where(d => d.AccountId == a.Id && d.Currency == currency)
                    .Select(d => (Guid?)d.WalletId)
                    .ToList()
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new AccountNumberNotFoundException(query.Number);

        return recipient.Wallets.SingleOrDefault() ?? throw new NoWalletInCurrencyException(currency);
    }
}
