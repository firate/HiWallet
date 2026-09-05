using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Balances;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Infrastructure.Persistence;

public sealed class WalletDbContext : DbContext
{
    public WalletDbContext(DbContextOptions<WalletDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();

    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<LedgerBalance> LedgerBalances => Set<LedgerBalance>();

    /// <summary>Tüketici tarafı idempotency defteri (overview.md madde 5).</summary>
    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
    }
}
