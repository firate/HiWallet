using HiWallet.StaffAdmin.Domain;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.StaffAdmin.Infrastructure.Persistence;

/// <summary>
/// Personel yönetiminin kendi veritabanı (<c>hiwallet_staff_admin</c>), kendi Postgres
/// sunucusunda. Burada yalnızca değişikliklerin kaydı var; çalışanlar, roller ve izinler
/// kimlik sağlayıcıda.
///
/// İki rol: şemanın sahibi (<c>staff_admin_owner</c>, migration) ve uygulama
/// (<c>staff_admin_app</c>). Kayıt üzerindeki <c>REVOKE</c> yalnızca tablo sahibi OLMAYAN
/// bir role işliyor.
/// </summary>
public sealed class StaffAdminDbContext(DbContextOptions<StaffAdminDbContext> options) : DbContext(options)
{
    public DbSet<StaffAuditEvent> AuditEvents => Set<StaffAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Configurations.StaffAuditEventConfiguration());
    }
}
