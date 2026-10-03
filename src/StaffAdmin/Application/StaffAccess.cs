using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Domain;
using HiWallet.StaffAdmin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Çalışanın şu anki rolleri ve izinleri. Her istekte okunuyor, önbelleğe alınmıyor:
/// rolü alınan ya da kapatılan çalışanın bir sonraki isteği reddediliyor. Token'da izin
/// yok; token yalnızca kim olduğunu söylüyor.
///
/// Çalışan ilk kez burada görüldüğünde davetini tamamlamış sayılıyor: geçerli bir token'ı
/// var, demek ki parolasını ve OTP'sini kurup giriş yaptı.
/// </summary>
public sealed class StaffAccess(IDbContextFactory<StaffAdminDbContext> contexts, TimeProvider time)
{
    /// <returns>Kapatılmış ya da panelden açılmamış çalışanda boş.</returns>
    public async Task<StaffAccessView> OfAsync(string subject, CancellationToken ct)
    {
        if (!Guid.TryParse(subject, out var staffId))
        {
            return StaffAccessView.None;
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        var member = await db.Members.SingleOrDefaultAsync(m => m.Id == staffId, ct);

        if (member is not { Enabled: true })
        {
            return StaffAccessView.None;
        }

        if (member.MarkSeen(time.GetUtcNow()))
        {
            await db.SaveChangesAsync(ct);
        }

        var roles = await db.Roles.AsNoTracking()
            .Where(r => db.RoleAssignments.Any(a => a.StaffId == staffId && a.RoleId == r.Id))
            .OrderBy(r => r.NormalizedName)
            .Select(r => new { r.Name, r.Permissions })
            .ToListAsync(ct);

        var granted = roles.SelectMany(r => r.Permissions).ToHashSet(StringComparer.Ordinal);

        return new StaffAccessView(
            [.. roles.Select(r => r.Name)],
            [.. StaffPermissions.All.Where(granted.Contains)]);
    }
}

/// <param name="Roles">Rollerin adları, ada göre.</param>
/// <param name="Permissions">Rollerden gelen izinler; tekrarsız ve kodun sırasıyla.</param>
public sealed record StaffAccessView(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)
{
    public static readonly StaffAccessView None = new([], []);
}
