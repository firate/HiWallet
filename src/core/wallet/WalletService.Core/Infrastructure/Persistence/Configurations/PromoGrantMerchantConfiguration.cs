using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Promos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Partinin geçerli olduğu işyeri hesapları (decisions.md madde 37). Satır
/// değişmiyor; UPDATE ve DELETE migration içinde REVOKE ediliyor.
/// Şema: docs/ledger-schema.md "promo_grants".
/// </summary>
internal sealed class PromoGrantMerchantConfiguration : IEntityTypeConfiguration<PromoGrantMerchant>
{
    public void Configure(EntityTypeBuilder<PromoGrantMerchant> builder)
    {
        builder.ToTable("promo_grant_merchants");

        // PK (grant_id, account_id) ödemenin "bu parti bu işyerinde geçerli mi"
        // sorusunu tek aramayla cevaplıyor.
        builder.HasKey(m => new { m.GrantId, m.AccountId }).HasName("pk_promo_grant_merchants");

        builder.Property(m => m.GrantId).HasColumnName("grant_id");
        builder.Property(m => m.AccountId).HasColumnName("account_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(m => m.AccountId)
            .HasConstraintName("fk_promo_grant_merchants_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.AccountId).HasDatabaseName("ix_promo_grant_merchants_account");
    }
}
