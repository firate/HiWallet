using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Promo partisi (decisions.md madde 37). Satır değişmiyor: UPDATE ve DELETE migration
/// içinde <c>wallet_app</c>'ten REVOKE ediliyor, <c>ledger_entries</c> ile aynı kalıp.
/// Şema: docs/ledger-schema.md "promo_grants".
/// </summary>
internal sealed class PromoGrantConfiguration : IEntityTypeConfiguration<PromoGrant>
{
    public void Configure(EntityTypeBuilder<PromoGrant> builder)
    {
        builder.ToTable("promo_grants", t =>
        {
            t.HasCheckConstraint("ck_promo_grants_amount", "amount > 0");
            t.HasCheckConstraint("ck_promo_grants_funder", "funder IN ('platform','business')");
            t.HasCheckConstraint("ck_promo_grants_funder_account",
                "(funder = 'business') = (funder_ledger_account_id IS NOT NULL)");
            t.HasCheckConstraint("ck_promo_grants_scope",
                "scope IN ('all_businesses','selected_businesses')");
            t.HasCheckConstraint("ck_promo_grants_expires_at",
                "expires_at IS NULL OR expires_at > created_at");
        });

        builder.HasKey(g => g.Id).HasName("pk_promo_grants");

        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.LedgerAccountId).HasColumnName("ledger_account_id").IsRequired();

        builder.Property(g => g.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(g => g.Currency)
            .HasColumnName("currency")
            .HasConversion(ValueConverters.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(g => g.Funder)
            .HasColumnName("funder")
            .HasConversion(ValueConverters.PromoFunder)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(g => g.FunderLedgerAccountId).HasColumnName("funder_ledger_account_id");

        builder.Property(g => g.Scope)
            .HasColumnName("scope")
            .HasConversion(ValueConverters.PromoScope)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(g => g.ExpiresAt).HasColumnName("expires_at");
        builder.Property(g => g.LedgerTransactionId).HasColumnName("ledger_transaction_id").IsRequired();

        builder.Property(g => g.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Ignore(g => g.Money);

        // Composite FK: partinin para birimi cüzdanınkinden sapamaz (decisions.md madde 17).
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(g => new { g.LedgerAccountId, g.Currency })
            .HasPrincipalKey(a => new { a.Id, a.Currency })
            .HasConstraintName("fk_promo_grants_ledger_account")
            .OnDelete(DeleteBehavior.Restrict);

        // Süre sonunda kalan fonlayana, aynı para biriminde dönüyor.
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(g => new { g.FunderLedgerAccountId, g.Currency })
            .HasPrincipalKey(a => new { a.Id, a.Currency })
            .HasConstraintName("fk_promo_grants_funder")
            .OnDelete(DeleteBehavior.Restrict);

        // Bir ledger işlemi en fazla bir parti açar. Tekrar eden istek partiyi bu
        // kolondan buluyor.
        builder.HasOne<LedgerTransaction>()
            .WithMany()
            .HasForeignKey(g => g.LedgerTransactionId)
            .HasConstraintName("fk_promo_grants_transaction")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => g.LedgerTransactionId)
            .HasDatabaseName("ux_promo_grants_transaction")
            .IsUnique();

        // Ödeme cüzdanın partilerini okuyor. Composite FK'nın index'i de bu işi görüyor;
        // adı verilmezse şemada PascalCase kalıyor.
        builder.HasIndex(g => new { g.LedgerAccountId, g.Currency })
            .HasDatabaseName("ix_promo_grants_ledger_account");

        builder.HasIndex(g => new { g.FunderLedgerAccountId, g.Currency })
            .HasDatabaseName("ix_promo_grants_funder");

        // Süre sonu işi yalnızca bitiş tarihi olan partilere bakıyor.
        builder.HasIndex(g => g.ExpiresAt)
            .HasDatabaseName("ix_promo_grants_expires_at")
            .HasFilter("expires_at IS NOT NULL");

        builder.HasMany(g => g.Merchants)
            .WithOne()
            .HasForeignKey(m => m.GrantId)
            .HasConstraintName("fk_promo_grant_merchants_grant")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(g => g.Merchants)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_merchants");
    }
}
