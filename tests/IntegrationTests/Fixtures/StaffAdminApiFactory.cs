using HiWallet.StaffAdmin;
using HiWallet.StaffAdmin.Application.Abstractions;
using HiWallet.StaffAdmin.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Personel yönetimini gerçek haliyle ayağa kaldırır; çalışanların kimlik sağlayıcısının
/// yerinde <see cref="FakeStaffDirectory"/> duruyor, veritabanı gerçek. Açılıştaki kurulum
/// (yönetici rolü, ilk yönetici) bu ikisine karşı koşuyor.
/// </summary>
/// <param name="bootstrapAdminEmail">Verilirse kurulum bu adrese ilk yönetici daveti gönderiyor.</param>
public sealed class StaffAdminApiFactory(StaffAdminFixture fixture, string? bootstrapAdminEmail = null)
    : WebApplicationFactory<StaffAdminApp>
{
    public const string AdminRoleName = "Personel yöneticisi";

    public FakeStaffDirectory Directory { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StaffAdmin"] = fixture.ConnectionString,
                // Kimlik sağlayıcıya gidilmiyor (sahte aşağıda) ama ayar başlangıçta doğrulanıyor.
                ["Keycloak:ClientSecret"] = "test-gizli-anahtar",
                ["StaffAdmin:Bootstrap:AdminRoleName"] = AdminRoleName,
                ["StaffAdmin:Bootstrap:AdminEmail"] = bootstrapAdminEmail ?? string.Empty
            });

            config.AddInMemoryCollection(TestTokens.Settings);
        });

        builder.ConfigureTestServices(services =>
        {
            TestTokens.Trust(services);
            services.AddSingleton<IStaffDirectory>(Directory);
        });
    }

    /// <summary>Açılıştaki kurulum bitene kadar bekler: yönetici rolü veritabanında.</summary>
    public async Task<HttpClient> CreateReadyClientAsync(CancellationToken ct)
    {
        var client = CreateClient();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var adminRole = StaffRole.NormalizeName(AdminRoleName);

        while (true)
        {
            await using (var db = fixture.CreateContext())
            {
                if (await db.Roles.AnyAsync(r => r.NormalizedName == adminRole, ct)
                    && (bootstrapAdminEmail is null || !Directory.Invitations.IsEmpty))
                {
                    return client;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Personel yönetiminin açılış kurulumu bitmedi.");
            }

            await Task.Delay(50, ct);
        }
    }

    /// <summary>
    /// Testin çalışanı: kimlik sağlayıcıda kullanıcı, personel yönetiminde kaydı ve verilen
    /// rolleri. Kimliği token'daki <c>sub</c>.
    /// </summary>
    public async Task<Guid> AddStaffAsync(CancellationToken ct, params string[] roleNames)
    {
        var email = $"calisan-{Guid.NewGuid():N}@ornek.com";
        var id = Directory.AddUser(email);

        await using var db = fixture.CreateContext();
        db.Members.Add(StaffMember.Invite(id, email, null, null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);

        await AssignAsync(id, ct, roleNames);

        return id;
    }

    /// <summary>Personel yönetimi izni olan çalışan: kurulumun açtığı yönetici rolüyle.</summary>
    public Task<Guid> AddAdminAsync(CancellationToken ct) => AddStaffAsync(ct, AdminRoleName);

    /// <summary>Panelden geçmeden rol verir: kendi rolünü değiştiremeyen yöneticinin durumu için.</summary>
    public async Task AssignAsync(Guid staffId, CancellationToken ct, params string[] roleNames)
    {
        await using var db = fixture.CreateContext();

        foreach (var name in roleNames)
        {
            var normalized = StaffRole.NormalizeName(name);
            var role = await db.Roles.SingleAsync(r => r.NormalizedName == normalized, ct);
            db.RoleAssignments.Add(StaffRoleAssignment.For(staffId, role.Id));
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> RoleNamesOfAsync(Guid staffId, CancellationToken ct)
    {
        await using var db = fixture.CreateContext();

        return await db.Roles
            .Where(r => db.RoleAssignments.Any(a => a.StaffId == staffId && a.RoleId == r.Id))
            .OrderBy(r => r.NormalizedName)
            .Select(r => r.Name)
            .ToListAsync(ct);
    }
}
