using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Partiden tüketim (decisions.md madde 37). Append-only: UPDATE ve DELETE migration
/// içinde <c>wallet_app</c>'ten REVOKE ediliyor.
/// Şema: docs/ledger-schema.md "promo_consumptions".
/// </summary>
internal sealed class PromoConsumptionConfiguration : IEntityTypeConfiguration<PromoConsumption>
{
    public void Configure(EntityTypeBuilder<PromoConsumption> builder)
    {
        builder.ToTable("promo_consumptions", t =>
            t.HasCheckConstraint("ck_promo_consumptions_amount", "amount > 0"));

        builder.HasKey(c => c.Id).HasName("pk_promo_consumptions");

        builder.Property(c => c.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(c => c.GrantId).HasColumnName("grant_id").IsRequired();
        builder.Property(c => c.LedgerTransactionId).HasColumnName("ledger_transaction_id").IsRequired();

        builder.Property(c => c.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<PromoGrant>()
            .WithMany()
            .HasForeignKey(c => c.GrantId)
            .HasConstraintName("fk_promo_consumptions_grant")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerTransaction>()
            .WithMany()
            .HasForeignKey(c => c.LedgerTransactionId)
            .HasConstraintName("fk_promo_consumptions_transaction")
            .OnDelete(DeleteBehavior.Restrict);

        // Bir işlem bir partiden en fazla bir kez tüketir. grant_id ile başladığı için
        // partinin kalanını toplayan sorgu da bu index'i kullanıyor.
        builder.HasIndex(c => new { c.GrantId, c.LedgerTransactionId })
            .HasDatabaseName("ux_promo_consumptions_grant_tx")
            .IsUnique();

        builder.HasIndex(c => c.LedgerTransactionId)
            .HasDatabaseName("ix_promo_consumptions_transaction");
    }
}
