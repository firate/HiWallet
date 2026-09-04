using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>Şema: docs/ledger-schema.md "ledger_transactions".</summary>
internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("ledger_transactions");

        builder.HasKey(t => t.Id).HasName("pk_ledger_transactions");

        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasConversion(ValueConverters.LedgerTransactionType)
            .HasColumnType("text")
            .IsRequired();

        // "İsteği başlatan hesap" değil, işlemin idempotency KAPSAMI olan hesap.
        // İç işlemlerde de dolu — nullable olsaydı unique index'teki NULL'lar eşleşmez
        // ve aynı fatura iki kez yazılabilirdi (decisions.md madde 15).
        builder.Property(t => t.LedgerAccountId).HasColumnName("ledger_account_id").IsRequired();

        builder.Property(t => t.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("text");
        builder.Property(t => t.CorrelationId).HasColumnName("correlation_id");

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(t => t.LedgerAccountId)
            .HasConstraintName("fk_ledger_transactions_ledger_account")
            .OnDelete(DeleteBehavior.Restrict);

        // Partial: idempotency key'siz iç işlemler çakışmaz.
        builder.HasIndex(t => new { t.LedgerAccountId, t.IdempotencyKey })
            .HasDatabaseName("ux_ledger_tx_idem")
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");

        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TransactionId)
            .HasConstraintName("fk_ledger_entries_transaction")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(t => t.Entries)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_entries");
    }
}
