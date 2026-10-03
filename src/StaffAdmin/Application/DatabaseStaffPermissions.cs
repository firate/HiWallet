using System.Security.Claims;
using HiWallet.Shared.Infrastructure.Authentication;

namespace HiWallet.StaffAdmin.Application;

/// <summary>Bu servisin kendi uçları için: izin veritabanından, aracısız.</summary>
internal sealed class DatabaseStaffPermissions(StaffAccess access) : IStaffPermissions
{
    public async Task<IReadOnlySet<string>> OfAsync(ClaimsPrincipal staff, CancellationToken ct) =>
        (await access.OfAsync(staff.Subject(), ct)).Permissions.ToHashSet(StringComparer.Ordinal);
}
