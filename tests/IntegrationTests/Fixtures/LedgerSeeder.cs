using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Balances;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Test verisi kurar. Para yaratmak için de ledger kullanılır — doğrudan bakiye yazmak
/// zero-sum invariant'ını bozardı ve testin kendisi kanıtlamaya çalıştığı şeyi delerdi.
/// </summary>
public static class LedgerSeeder
{
    private static readonly DateTimeOffset SeedTime = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    public static async Task<Guid> CreateAccountAsync(
        WalletDbContext db, AccountType type, CancellationToken ct)
    {
        var account = Account.Open(Guid.NewGuid(), type, SeedTime);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }

    public static async Task<Guid> CreateWalletAsync(
        WalletDbContext db, Guid accountId, string name, CancellationToken ct)
    {
        var wallet = LedgerAccount.Wallet(
            Guid.NewGuid(), accountId, name, SystemAccounts.DefaultCurrency, SeedTime);

        db.LedgerAccounts.Add(wallet);
        db.LedgerBalances.Add(LedgerBalance.OpenFor(
            wallet.Id, SystemAccounts.DefaultCurrency, SeedTime));

        await db.SaveChangesAsync(ct);
        return wallet.Id;
    }

    /// <summary>
    /// Cüzdana para koyar: cüzdan <c>+X</c>, clearing <c>-X</c> — top-up akışının
    /// ledger karşılığı (overview.md madde 3). Dengeli olduğu için trigger'dan geçer.
    /// </summary>
    public static async Task FundAsync(
        WalletDbContext db, Guid walletId, decimal amount, CancellationToken ct)
    {
        var currency = SystemAccounts.DefaultCurrency;
        var clearingId = SystemAccounts.ClearingStripeTry;

        var tx = LedgerTransaction
            .Create(Guid.NewGuid(), LedgerTransactionType.Topup, walletId, SeedTime)
            .AddEntry(walletId, new Money(amount, currency))
            .AddEntry(clearingId, new Money(-amount, currency));

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        foreach (var (id, delta) in new[] { (walletId, amount), (clearingId, -amount) }
                     .OrderBy(x => x.Item1))
        {
            var balance = await db.LedgerBalances.FindAsync([id], ct)
                          ?? throw new InvalidOperationException($"Bakiye satırı yok: {id}");

            balance.Apply(new Money(delta, currency), canGoNegative: id == clearingId, SeedTime);
        }

        await db.SaveChangesAsync(ct);
    }
}
