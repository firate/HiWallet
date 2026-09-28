using HiWallet.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.Onboarding.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Subject).HasName("pk_customers");

        builder.Property(c => c.Subject).HasColumnName("subject").HasColumnType("text");
        builder.Property(c => c.Phone)
            .HasColumnName("phone")
            .HasConversion(ValueConverters.PhoneNumber)
            .HasColumnType("text");
        builder.Property(c => c.PhoneVerifiedAt).HasColumnName("phone_verified_at");
        builder.Property(c => c.FirstName).HasColumnName("first_name").HasColumnType("text");
        builder.Property(c => c.LastName).HasColumnName("last_name").HasColumnType("text");
        builder.Property(c => c.NationalId)
            .HasColumnName("national_id")
            .HasConversion(ValueConverters.NationalId)
            .HasColumnType("char(11)");
        builder.Property(c => c.BirthDate).HasColumnName("birth_date");
        builder.Property(c => c.IdentityVerifiedAt).HasColumnName("identity_verified_at");
        builder.Property(c => c.BasicVerifiedAt).HasColumnName("basic_verified_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        // Bir kimlik numarası tek müşteriye ait: aynı kişi ikinci bir hesabı kendi
        // kimliğiyle doğrulayamıyor.
        builder.HasIndex(c => c.NationalId)
            .HasDatabaseName("ux_customers_national_id")
            .IsUnique()
            .HasFilter("national_id IS NOT NULL");
    }
}
