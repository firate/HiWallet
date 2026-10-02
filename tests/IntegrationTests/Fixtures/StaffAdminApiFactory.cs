using HiWallet.StaffAdmin;
using HiWallet.StaffAdmin.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Personel yönetimini gerçek haliyle ayağa kaldırır; çalışanların kimlik sağlayıcısının
/// yerinde <see cref="FakeStaffDirectory"/> duruyor. Açılıştaki kurulum (izinler, yönetici
/// rolü, ilk yönetici) bu sahteye karşı koşuyor.
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

    /// <summary>Açılıştaki kurulum bitene kadar bekler: yönetici rolü dizinde.</summary>
    public async Task<HttpClient> CreateReadyClientAsync(CancellationToken ct)
    {
        var client = CreateClient();
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (Directory.RoleNamed(AdminRoleName) is null
               || (bootstrapAdminEmail is not null && Directory.Invitations.IsEmpty))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Personel yönetiminin açılış kurulumu bitmedi.");
            }

            await Task.Delay(50, ct);
        }

        return client;
    }
}
