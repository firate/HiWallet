using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Application.Abstractions;
using HiWallet.StaffAdmin.Domain;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Panelin rolleri: kodun izinlerinden kurulan setler. Rolün içeriği değişince o roldeki
/// herkesin yetkisi değişiyor. Çalışan sahip olduğu rolü değiştiremiyor ve silemiyor;
/// yoksa kendine yetki vermiş olurdu.
/// </summary>
public sealed class RoleService(IStaffDirectory directory, StaffAudit audit)
{
    public async Task<IReadOnlyList<DirectoryRole>> ListAsync(CancellationToken ct) =>
    [
        .. (await directory.ListRolesAsync(ct))
            .Where(r => r.Kind is DirectoryRoleKind.StaffRole)
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
    ];

    public async Task<(DirectoryRole Role, IReadOnlyList<DirectoryUser> Members)> GetAsync(Guid roleId, CancellationToken ct)
    {
        var role = await RequireAsync(roleId, ct);

        return (role, await directory.RoleMembersAsync(roleId, ct));
    }

    public async Task<DirectoryRole> CreateAsync(
        Actor actor, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        if (StaffRoleRules.IsReserved(name))
        {
            throw new StaffAdminRuleException(
                StaffAdminRules.RoleNameReserved, "Bu ad bir iznin ya da kimlik sağlayıcının kendi rolünün adı.");
        }

        var existing = await directory.ListRolesAsync(ct);

        if (existing.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new StaffAdminConflictException(StaffAdminRules.RoleExists, "Bu adda bir rol var.");
        }

        var ordered = InCodeOrder(permissions);
        Guid roleId;

        try
        {
            roleId = await directory.CreateRoleAsync(name, description ?? string.Empty, ordered, ct);
        }
        catch (DirectoryConflictException)
        {
            throw new StaffAdminConflictException(StaffAdminRules.RoleExists, "Bu adda bir rol var.");
        }

        await audit.RecordAsync(
            actor, StaffAuditAction.RoleCreated, StaffAuditTarget.Role, roleId, name,
            new { description, permissions = ordered }, ct);

        return new DirectoryRole(roleId, name, description, DirectoryRoleKind.StaffRole, ordered);
    }

    public async Task<DirectoryRole> UpdateAsync(
        Actor actor, Guid roleId, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        var role = await RequireAsync(roleId, ct);
        await EnsureNotHeldByActorAsync(actor, role, ct);

        var ordered = InCodeOrder(permissions);
        await directory.UpdateRoleAsync(roleId, description ?? string.Empty, ordered, ct);

        await audit.RecordAsync(
            actor, StaffAuditAction.RoleUpdated, StaffAuditTarget.Role, roleId, role.Name,
            new
            {
                before = new { role.Description, role.Permissions },
                after = new { description, permissions = ordered }
            }, ct);

        return role with { Description = description, Permissions = ordered };
    }

    public async Task DeleteAsync(Actor actor, Guid roleId, CancellationToken ct)
    {
        var role = await RequireAsync(roleId, ct);
        await EnsureNotHeldByActorAsync(actor, role, ct);

        // Silinen rolün atamaları da gidiyor; kimin yetkisini kaybettiği kayıtta kalsın.
        var members = await directory.RoleMembersAsync(roleId, ct);
        await directory.DeleteRoleAsync(roleId, ct);

        await audit.RecordAsync(
            actor, StaffAuditAction.RoleDeleted, StaffAuditTarget.Role, roleId, role.Name,
            new { role.Permissions, members = members.Select(m => m.Email) }, ct);
    }

    private async Task<DirectoryRole> RequireAsync(Guid roleId, CancellationToken ct) =>
        await directory.FindRoleAsync(roleId, ct) is { Kind: DirectoryRoleKind.StaffRole } role
            ? role
            : throw new StaffAdminNotFoundException("Rol bulunamadı.");

    private async Task EnsureNotHeldByActorAsync(Actor actor, DirectoryRole role, CancellationToken ct)
    {
        if (actor.Id is { } actorId && (await directory.UserRoleIdsAsync(actorId, ct)).Contains(role.Id))
        {
            throw new StaffAdminRuleException(
                StaffAdminRules.OwnRole, "Sahip olduğun rolü değiştiremez ve silemezsin; başka bir yönetici yapmalı.");
        }
    }

    /// <summary>Tekrarsız ve kodun sırasıyla: kayıt ve panel hep aynı sırayı görsün.</summary>
    private static IReadOnlyList<string> InCodeOrder(IReadOnlyCollection<string> permissions) =>
        [.. StaffPermissions.All.Where(permissions.Contains)];
}
