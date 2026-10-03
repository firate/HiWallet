using HiWallet.StaffAdmin.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.StaffAdmin.Infrastructure.Persistence.Configurations;

internal sealed class StaffRoleConfiguration : IEntityTypeConfiguration<StaffRole>
{
    public void Configure(EntityTypeBuilder<StaffRole> builder)
    {
        builder.ToTable("staff_roles");

        builder.HasKey(r => r.Id).HasName("pk_staff_roles");

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.Name).HasColumnName("name").HasColumnType("text").IsRequired();
        builder.Property(r => r.NormalizedName).HasColumnName("normalized_name").HasColumnType("text").IsRequired();
        builder.Property(r => r.Description).HasColumnName("description").HasColumnType("text");
        // İzinler kodda tanımlı ve sayıları küçük; rolün satırında dizi olarak duruyor.
        builder.Property(r => r.Permissions).HasColumnName("permissions").HasColumnType("text[]").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(r => r.NormalizedName).IsUnique().HasDatabaseName("ux_staff_roles_normalized_name");
    }
}
