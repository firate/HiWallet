using HiWallet.WalletService.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>Hesabın kullanıcıları. Şema: docs/ledger-schema.md "account_members".</summary>
internal sealed class AccountMemberConfiguration : IEntityTypeConfiguration<AccountMember>
{
    public void Configure(EntityTypeBuilder<AccountMember> builder)
    {
        builder.ToTable("account_members");

        // PK (subject, account_id): her istekte sorulan soru "bu kimliğin hesapları
        // hangileri", yani arama kimlikten başlıyor.
        builder.HasKey(m => new { m.Subject, m.AccountId }).HasName("pk_account_members");

        builder.Property(m => m.Subject).HasColumnName("subject").HasColumnType("text");
        builder.Property(m => m.AccountId).HasColumnName("account_id");

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(m => m.AccountId)
            .HasConstraintName("fk_account_members_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.AccountId).HasDatabaseName("ix_account_members_account");
    }
}
