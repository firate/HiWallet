using System.Collections.Concurrent;
using System.Security.Claims;
using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Testlerin personel yönetimi. Çalışanın izinleri canlıdaki gibi token'da değil:
/// <see cref="TestTokens.AsStaff"/> izni buraya yazıyor, wallet-api ve orchestrator her
/// istekte buradan okuyor. Kimlikler test başına yeni; testler birbirinin iznini görmüyor.
///
/// Personel yönetimini gerçek haliyle konuşan testler bunun yerine
/// <see cref="StaffAdminApiFactory"/>'yi bağlıyor.
/// </summary>
public static class TestStaffPermissions
{
    private static readonly ConcurrentDictionary<string, string[]> Granted = new();

    public static void Grant(string subject, params string[] permissions) => Granted[subject] = permissions;

    /// <summary>wallet-api ve orchestrator'ın izin kaynağı bu kaydın yerine.</summary>
    public static void Use(IServiceCollection services) =>
        services.AddScoped<IStaffPermissions, RegistryPermissions>();

    private sealed class RegistryPermissions : IStaffPermissions
    {
        public Task<IReadOnlySet<string>> OfAsync(ClaimsPrincipal staff, CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<string>>(
                (Granted.GetValueOrDefault(staff.Subject()) ?? []).ToHashSet(StringComparer.Ordinal));
    }
}
