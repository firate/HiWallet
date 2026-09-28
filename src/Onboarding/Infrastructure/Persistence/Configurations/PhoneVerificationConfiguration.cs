using HiWallet.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.Onboarding.Infrastructure.Persistence.Configurations;

internal sealed class PhoneVerificationConfiguration : IEntityTypeConfiguration<PhoneVerification>
{
    public void Configure(EntityTypeBuilder<PhoneVerification> builder)
    {
        builder.ToTable("phone_verifications");

        builder.HasKey(v => v.Id).HasName("pk_phone_verifications");

        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.Subject).HasColumnName("subject").HasColumnType("text").IsRequired();
        builder.Property(v => v.Phone)
            .HasColumnName("phone")
            .HasConversion(ValueConverters.PhoneNumber)
            .HasColumnType("text")
            .IsRequired();
        builder.Property(v => v.CodeHash).HasColumnName("code_hash").HasColumnType("text").IsRequired();
        builder.Property(v => v.ExpiresAt).HasColumnName("expires_at");
        builder.Property(v => v.FailedAttempts).HasColumnName("failed_attempts");
        builder.Property(v => v.VerifiedAt).HasColumnName("verified_at");
        builder.Property(v => v.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(v => v.Subject).HasDatabaseName("ix_phone_verifications_subject");
    }
}
