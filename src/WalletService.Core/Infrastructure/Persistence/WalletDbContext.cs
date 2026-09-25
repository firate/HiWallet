using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Balances;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
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

    /// <summary>Promo partileri (decisions.md madde 37). Satır değişmiyor.</summary>
    public DbSet<PromoGrant> PromoGrants => Set<PromoGrant>();

    public DbSet<PromoGrantMerchant> PromoGrantMerchants => Set<PromoGrantMerchant>();

    /// <summary>Partilerden tüketim. Append-only.</summary>
    public DbSet<PromoConsumption> PromoConsumptions => Set<PromoConsumption>();

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

    /// <summary>
    /// Dönem sonu faturaları (decisions.md madde 11). Uygulanmış faturanın satırları
    /// provider_fees'te; bu tablo faturanın KENDİSİNİ tutuyor — PendingReview'da
    /// bekleyenin hiç satırı olmadığı için gruplanacak bir şey de yok.
    /// </summary>
    internal DbSet<ProviderInvoice> ProviderInvoices => Set<ProviderInvoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
    }
}
