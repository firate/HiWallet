using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Promos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Kampanyaya bağlı işyeri hesapları: tetikleyenler ve verilen partinin kapsamı.
/// Şema: docs/ledger-schema.md "promo_campaigns".
/// </summary>
internal sealed class PromoCampaignMerchantConfiguration : IEntityTypeConfiguration<PromoCampaignMerchant>
{
    public void Configure(EntityTypeBuilder<PromoCampaignMerchant> builder)
    {
        builder.ToTable("promo_campaign_merchants", t =>
            t.HasCheckConstraint("ck_promo_campaign_merchants_role", "role IN ('trigger','scope')"));

        builder.HasKey(m => new { m.CampaignId, m.Role, m.AccountId }).HasName("pk_promo_campaign_merchants");

        builder.Property(m => m.CampaignId).HasColumnName("campaign_id");
        builder.Property(m => m.AccountId).HasColumnName("account_id");

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasConversion(ValueConverters.PromoCampaignMerchantRole)
            .HasColumnType("text");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(m => m.AccountId)
            .HasConstraintName("fk_promo_campaign_merchants_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.AccountId).HasDatabaseName("ix_promo_campaign_merchants_account");
    }
}
