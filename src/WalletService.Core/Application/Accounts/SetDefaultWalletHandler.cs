using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class SetDefaultWalletHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task HandleAsync(SetDefaultWalletCommand command, CancellationToken ct)
    {
        var currency = Currency.From(command.Currency);

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // Başka bir hesabın cüzdanı da yok sayılıyor: varlığı söylenmiyor.
        var wallet = await db.LedgerAccounts
                         .AsNoTracking()
                         .SingleOrDefaultAsync(w =>
                             w.Id == command.WalletId
                             && w.Type == LedgerAccountType.UserWallet
                             && w.AccountId == command.AccountId, ct)
                     ?? throw new WalletNotFoundException(command.WalletId);

        if (wallet.Currency != currency)
        {
            throw new AccountRuleException($"Varsayılan {currency} cüzdanı {currency} cüzdanlarından biri olmalı.");
        }

        var chosen = DefaultWallet.Of(wallet);

        // Tek satır: eşzamanlı iki seçimden sonuncusu kalıyor, arada varsayılansız an yok.
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO default_wallets (account_id, currency, wallet_id)
             VALUES ({chosen.AccountId}, {chosen.Currency.Code}, {chosen.WalletId})
             ON CONFLICT (account_id, currency) DO UPDATE SET wallet_id = EXCLUDED.wallet_id
             """,
            ct);
    }
}
