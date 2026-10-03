using HiWallet.StaffAdmin.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.StaffAdmin.Infrastructure.Persistence.Configurations;

internal sealed class StaffRoleAssignmentConfiguration : IEntityTypeConfiguration<StaffRoleAssignment>
{
    public void Configure(EntityTypeBuilder<StaffRoleAssignment> builder)
    {
        builder.ToTable("staff_role_assignments");

        builder.HasKey(a => new { a.StaffId, a.RoleId }).HasName("pk_staff_role_assignments");

        builder.Property(a => a.StaffId).HasColumnName("staff_id");
        builder.Property(a => a.RoleId).HasColumnName("role_id");

        builder.HasOne<StaffMember>()
            .WithMany()
            .HasForeignKey(a => a.StaffId)
            .HasConstraintName("fk_staff_role_assignments_staff_members")
            .OnDelete(DeleteBehavior.Restrict);

        // Silinen rolün atamaları da gidiyor; kimin yetkisini kaybettiği kayıtta.
        builder.HasOne<StaffRole>()
            .WithMany()
            .HasForeignKey(a => a.RoleId)
            .HasConstraintName("fk_staff_role_assignments_staff_roles")
            .OnDelete(DeleteBehavior.Cascade);

        // Bir rolün üyeleri.
        builder.HasIndex(a => a.RoleId).HasDatabaseName("ix_staff_role_assignments_role_id");
    }
}
