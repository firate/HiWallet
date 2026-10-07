using HiWallet.StaffAdmin.Domain;
using HiWallet.StaffAdmin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Panelin rolleri: kodun izinlerinden kurulan setler. Rolün içeriği değişince o roldeki
/// herkesin yetkisi bir sonraki istekte değişiyor. Çalışan sahip olduğu rolü değiştiremiyor
/// ve silemiyor; yoksa kendine yetki vermiş olurdu.
/// </summary>
public sealed class RoleService(
    IDbContextFactory<StaffAdminDbContext> contexts,
    StaffAudit audit,
    TimeProvider time)
{
    /// <summary>Rolün üyeleri tek parça dönüyor; tavan aşılırsa liste sessizce kesilmesin diye hata.</summary>
    private const int MemberLimit = 1000;

    public async Task<(IReadOnlyList<StaffRole> Items, bool HasMore)> ListAsync(int skip, int take, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var rows = await db.Roles.AsNoTracking()
            .OrderBy(r => r.NormalizedName)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);

        return ([.. rows.Take(take)], rows.Count > take);
    }

    public async Task<(StaffRole Role, IReadOnlyList<StaffMember> Members)> GetAsync(Guid roleId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var role = await RequireAsync(db, roleId, ct);

        var members = await db.Members.AsNoTracking()
            .Where(m => db.RoleAssignments.Any(a => a.RoleId == roleId && a.StaffId == m.Id))
            .OrderBy(m => m.Email)
            .Take(MemberLimit + 1)
            .ToListAsync(ct);

        if (members.Count > MemberLimit)
        {
            throw new InvalidOperationException($"Rolün {MemberLimit}'den fazla üyesi var; liste kesilirdi.");
        }

        return (role, members);
    }

    public async Task<StaffRole> CreateAsync(
        Actor actor, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var role = StaffRole.Create(name, description, permissions, time.GetUtcNow());
        db.Roles.Add(role);

        audit.Add(db, actor, StaffAuditAction.RoleCreated, StaffAuditTarget.Role, role.Id, role.Name,
            new { description, permissions = role.Permissions });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (UniqueViolation.Is(exception))
        {
            throw new StaffAdminConflictException(StaffAdminRules.RoleExists, "Bu adda bir rol var.");
        }

        return role;
    }

    public async Task<StaffRole> UpdateAsync(
        Actor actor, Guid roleId, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var role = await RequireAsync(db, roleId, ct);
        await EnsureNotHeldByActorAsync(db, actor, roleId, ct);

        var before = new { role.Description, Permissions = role.Permissions.ToList() };
        role.Change(description, permissions);

        audit.Add(db, actor, StaffAuditAction.RoleUpdated, StaffAuditTarget.Role, role.Id, role.Name,
            new { before, after = new { role.Description, role.Permissions } });

        await db.SaveChangesAsync(ct);

        return role;
    }

    public async Task DeleteAsync(Actor actor, Guid roleId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var role = await RequireAsync(db, roleId, ct);
        await EnsureNotHeldByActorAsync(db, actor, roleId, ct);

        // Atamalar rolle birlikte gidiyor (cascade); kimin yetkisini kaybettiği kayıtta kalsın.
        var members = await db.Members.AsNoTracking()
            .Where(m => db.RoleAssignments.Any(a => a.RoleId == roleId && a.StaffId == m.Id))
            .OrderBy(m => m.Email)
            .Select(m => m.Email)
            .ToListAsync(ct);

        db.Roles.Remove(role);

        audit.Add(db, actor, StaffAuditAction.RoleDeleted, StaffAuditTarget.Role, role.Id, role.Name,
            new { role.Permissions, members });

        await db.SaveChangesAsync(ct);
    }

    private static async Task<StaffRole> RequireAsync(StaffAdminDbContext db, Guid roleId, CancellationToken ct) =>
        await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId, ct)
        ?? throw new StaffAdminNotFoundException("Rol bulunamadı.");

    private static async Task EnsureNotHeldByActorAsync(
        StaffAdminDbContext db, Actor actor, Guid roleId, CancellationToken ct)
    {
        if (actor.Id is { } actorId
            && await db.RoleAssignments.AnyAsync(a => a.StaffId == actorId && a.RoleId == roleId, ct))
        {
            throw new StaffAdminRuleException(
                StaffAdminRules.OwnRole, "Sahip olduğun rolü değiştiremez ve silemezsin; başka bir yönetici yapmalı.");
        }
    }
}
