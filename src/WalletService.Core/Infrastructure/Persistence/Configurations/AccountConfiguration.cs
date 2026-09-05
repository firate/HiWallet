using HiWallet.WalletService.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>Müşteri hesabı. Şema: docs/ledger-schema.md "accounts".</summary>
internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", t =>
            t.HasCheckConstraint("ck_accounts_type", "type IN ('person','business')"));

        builder.HasKey(a => a.Id).HasName("pk_accounts");

        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.Type)
            .HasColumnName("type")
            .HasConversion(ValueConverters.AccountType)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
    }
}
