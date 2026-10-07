using HiWallet.StaffAdmin.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.StaffAdmin.Infrastructure.Persistence.Configurations;

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("staff_members");

        builder.HasKey(m => m.Id).HasName("pk_staff_members");

        // Kimlik sağlayıcıdaki kullanıcının kimliği; burada üretilmiyor.
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(m => m.Email).HasColumnName("email").HasColumnType("text").IsRequired();
        builder.Property(m => m.FirstName).HasColumnName("first_name").HasColumnType("text");
        builder.Property(m => m.LastName).HasColumnName("last_name").HasColumnType("text");
        builder.Property(m => m.Enabled).HasColumnName("enabled");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.ActivatedAt).HasColumnName("activated_at");

        builder.HasIndex(m => m.Email).IsUnique().HasDatabaseName("ux_staff_members_email");
    }
}
