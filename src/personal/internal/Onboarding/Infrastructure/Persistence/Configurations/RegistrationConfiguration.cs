using HiWallet.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.Onboarding.Infrastructure.Persistence.Configurations;

internal sealed class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> builder)
    {
        builder.ToTable("registrations");

        builder.HasKey(r => r.Id).HasName("pk_registrations");

        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.Email).HasColumnName("email").HasColumnType("text").IsRequired();
        builder.Property(r => r.CodeHash).HasColumnName("code_hash").HasColumnType("text").IsRequired();
        builder.Property(r => r.CodeExpiresAt).HasColumnName("code_expires_at");
        builder.Property(r => r.FailedAttempts).HasColumnName("failed_attempts");
        builder.Property(r => r.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(r => r.Subject).HasColumnName("subject").HasColumnType("text");
        builder.Property(r => r.AccountId).HasColumnName("account_id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(r => r.Email).HasDatabaseName("ix_registrations_email");
        builder.HasIndex(r => r.Subject).HasDatabaseName("ix_registrations_subject");

        // Çalışan müşteriyi hesabından buluyor.
        builder.HasIndex(r => r.AccountId)
            .HasDatabaseName("ix_registrations_account_id")
            .HasFilter("account_id IS NOT NULL");
    }
}
