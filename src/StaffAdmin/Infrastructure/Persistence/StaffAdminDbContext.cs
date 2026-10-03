using HiWallet.StaffAdmin.Domain;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.StaffAdmin.Infrastructure.Persistence;

/// <summary>
/// Personel yönetiminin kendi veritabanı (<c>hiwallet_staff_admin</c>), kendi Postgres
/// sunucusunda. Çalışanlar, roller, atamalar ve değişikliklerin kaydı burada; bir
/// değişiklik ve kaydı aynı transaction'da yazılıyor. Kimlik sağlayıcıda yalnızca kullanıcı,
/// parola, OTP ve oturum var.
///
/// İki rol: şemanın sahibi (<c>staff_admin_owner</c>, migration) ve uygulama
/// (<c>staff_admin_app</c>). Kayıt üzerindeki <c>REVOKE</c> yalnızca tablo sahibi OLMAYAN
/// bir role işliyor.
/// </summary>
public sealed class StaffAdminDbContext(DbContextOptions<StaffAdminDbContext> options) : DbContext(options)
{
    public DbSet<StaffMember> Members => Set<StaffMember>();

    public DbSet<StaffRole> Roles => Set<StaffRole>();

    public DbSet<StaffRoleAssignment> RoleAssignments => Set<StaffRoleAssignment>();

    public DbSet<StaffAuditEvent> AuditEvents => Set<StaffAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Configurations.StaffMemberConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.StaffRoleConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.StaffRoleAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.StaffAuditEventConfiguration());
    }
}
