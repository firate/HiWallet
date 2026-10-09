using HiWallet.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.Onboarding.Infrastructure.Persistence.Configurations;

internal sealed class PhoneChangeConfiguration : IEntityTypeConfiguration<PhoneChange>
{
    public void Configure(EntityTypeBuilder<PhoneChange> builder)
    {
        builder.ToTable("phone_changes");

        builder.HasKey(c => c.Id).HasName("pk_phone_changes");

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.Subject).HasColumnName("subject").HasColumnType("text").IsRequired();
        builder.Property(c => c.OldPhone)
            .HasColumnName("old_phone")
            .HasConversion(ValueConverters.PhoneNumber)
            .HasColumnType("text");
        builder.Property(c => c.NewPhone)
            .HasColumnName("new_phone")
            .HasConversion(ValueConverters.PhoneNumber)
            .HasColumnType("text")
            .IsRequired();
        builder.Property(c => c.ChangedAt).HasColumnName("changed_at");

        builder.HasIndex(c => c.Subject).HasDatabaseName("ix_phone_changes_subject");
    }
}
