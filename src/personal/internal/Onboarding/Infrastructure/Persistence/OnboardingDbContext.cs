using HiWallet.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.Onboarding.Infrastructure.Persistence;

/// <summary>
/// onboarding'in kendi veritabanı (<c>hiwallet_onboarding</c>), kendi Postgres sunucusunda.
/// Kişisel veri burada: e-posta, telefon, TCKN, doğum tarihi, onaylar. Ledger'la aynı
/// sunucuda durmuyor; wallet'a yalnızca "hesabı aç" ve "seviyeyi yükselt" gidiyor.
///
/// İki rol: şemanın sahibi (<c>onboarding_owner</c>, migration) ve uygulama
/// (<c>onboarding_app</c>). Sebep wallet'takiyle aynı: <c>consents</c> üzerindeki
/// <c>REVOKE</c> yalnızca tablo sahibi OLMAYAN bir role işliyor.
/// </summary>
public sealed class OnboardingDbContext(DbContextOptions<OnboardingDbContext> options) : DbContext(options)
{
    public DbSet<Registration> Registrations => Set<Registration>();

    public DbSet<PhoneVerification> PhoneVerifications => Set<PhoneVerification>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Consent> Consents => Set<Consent>();

    public DbSet<PhoneChange> PhoneChanges => Set<PhoneChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Eşlemeler tek tek uygulanıyor, assembly taraması yapılmıyor: hangi tablonun
        // bu şemada olduğu tek bakışta görünsün.
        modelBuilder.ApplyConfiguration(new Configurations.RegistrationConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.PhoneVerificationConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.ConsentConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.PhoneChangeConfiguration());
    }
}
