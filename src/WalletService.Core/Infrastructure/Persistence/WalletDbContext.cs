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

    /// <summary>Top-up event'lerinin idempotency defteri (overview.md madde 5).</summary>
    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    /// <summary>Saga komutlarının idempotency defteri (decisions.md madde 32).</summary>
    internal DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    /// <summary>
    /// İşletme günlük özeti (overview.md madde 7). Ledger DEĞİL — türetilmiş rapor,
    /// her zaman yeniden hesaplanabilir.
    /// </summary>
    internal DbSet<BusinessDailySummary> BusinessDailySummaries => Set<BusinessDailySummary>();

    /// <summary>
    /// Sağlayıcı ücretinin beklenen/gerçekleşen takibi (decisions.md madde 10).
    /// Ledger DEĞİL: zero-sum'a dahil değil, expected_amount hiçbir zaman ledger'a
    /// yazılmıyor.
    /// </summary>
    internal DbSet<ProviderFee> ProviderFees => Set<ProviderFee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
    }
}
