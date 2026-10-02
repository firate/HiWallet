using HiWallet.StaffAdmin.Application.Abstractions;
using HiWallet.StaffAdmin.Domain;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Çalışanlar: davet, rol atama, kapatma. Çalışan yetkiyi yalnızca panelin rollerinden
/// alıyor; izin ya da kimlik sağlayıcının rolü doğrudan verilmiyor. Çalışan kendi
/// rollerini değiştiremiyor ve kendini kapatamıyor.
/// </summary>
public sealed class StaffService(IStaffDirectory directory, StaffAudit audit)
{
    public Task<DirectoryUserPage> ListAsync(int first, int size, string? search, CancellationToken ct) =>
        directory.ListUsersAsync(first, size, search, ct);

    public async Task<StaffDetail> GetAsync(Guid staffId, CancellationToken ct)
    {
        var user = await RequireAsync(staffId, ct);

        return new StaffDetail(user, await StaffRolesOfAsync(staffId, ct));
    }

    /// <summary>
    /// Kullanıcıyı açar, rollerini verir ve daveti gönderir. Parola ve OTP'yi çalışan
    /// davetteki bağlantıdan kendisi kuruyor; panel parolayı hiç görmüyor.
    /// </summary>
    public async Task<StaffDetail> InviteAsync(
        Actor actor, string email, string? firstName, string? lastName, IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct)
    {
        var roles = await ResolveStaffRolesAsync(roleIds, ct);

        if (await directory.FindUserByEmailAsync(email, ct) is not null)
        {
            throw new StaffAdminConflictException(StaffAdminRules.StaffExists, "Bu e-postayla bir çalışan var.");
        }

        Guid staffId;

        try
        {
            staffId = await directory.CreateUserAsync(email, firstName, lastName, ct);
        }
        catch (DirectoryConflictException)
        {
            throw new StaffAdminConflictException(StaffAdminRules.StaffExists, "Bu e-postayla bir çalışan var.");
        }

        if (roles.Count > 0)
        {
            await directory.AssignRolesAsync(staffId, roles, ct);
        }

        await directory.SendInvitationAsync(staffId, ct);

        await audit.RecordAsync(
            actor, StaffAuditAction.StaffInvited, StaffAuditTarget.Staff, staffId, email,
            new { roles = roles.Select(r => r.Name) }, ct);

        return await GetAsync(staffId, ct);
    }

    /// <summary>Çalışanın rollerinin tamamı; listede olmayanlar alınıyor.</summary>
    public async Task<StaffDetail> SetRolesAsync(
        Actor actor, Guid staffId, IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        EnsureNotSelf(actor, staffId);
        var user = await RequireAsync(staffId, ct);
        var desired = await ResolveStaffRolesAsync(roleIds, ct);
        var current = await StaffRolesOfAsync(staffId, ct);

        var added = desired.ExceptBy(current.Select(r => r.Id), r => r.Id).ToList();
        var removed = current.ExceptBy(desired.Select(r => r.Id), r => r.Id).ToList();

        if (added.Count == 0 && removed.Count == 0)
        {
            return new StaffDetail(user, current);
        }

        if (added.Count > 0)
        {
            await directory.AssignRolesAsync(staffId, added, ct);
        }

        if (removed.Count > 0)
        {
            await directory.UnassignRolesAsync(staffId, removed, ct);
        }

        await audit.RecordAsync(
            actor, StaffAuditAction.StaffRolesChanged, StaffAuditTarget.Staff, staffId, user.Email,
            new { before = current.Select(r => r.Name), after = desired.Select(r => r.Name) }, ct);

        return await GetAsync(staffId, ct);
    }

    /// <summary>Kapatılan çalışanın açık oturumları da kapanıyor.</summary>
    public async Task<StaffDetail> SetEnabledAsync(Actor actor, Guid staffId, bool enabled, CancellationToken ct)
    {
        EnsureNotSelf(actor, staffId);
        var user = await RequireAsync(staffId, ct);

        if (user.Enabled != enabled)
        {
            await directory.SetEnabledAsync(staffId, enabled, ct);

            await audit.RecordAsync(
                actor, enabled ? StaffAuditAction.StaffEnabled : StaffAuditAction.StaffDisabled,
                StaffAuditTarget.Staff, staffId, user.Email, new { }, ct);
        }

        return await GetAsync(staffId, ct);
    }

    public async Task ResendInvitationAsync(Actor actor, Guid staffId, CancellationToken ct)
    {
        var user = await RequireAsync(staffId, ct);
        await directory.SendInvitationAsync(staffId, ct);

        await audit.RecordAsync(
            actor, StaffAuditAction.InvitationSent, StaffAuditTarget.Staff, staffId, user.Email, new { }, ct);
    }

    private async Task<DirectoryUser> RequireAsync(Guid staffId, CancellationToken ct) =>
        await directory.FindUserAsync(staffId, ct) ?? throw new StaffAdminNotFoundException("Çalışan bulunamadı.");

    private async Task<IReadOnlyList<DirectoryRole>> ResolveStaffRolesAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var staffRoles = (await directory.ListRolesAsync(ct))
            .Where(r => r.Kind is DirectoryRoleKind.StaffRole)
            .ToDictionary(r => r.Id);

        if (roleIds.Any(id => !staffRoles.ContainsKey(id)))
        {
            throw new StaffAdminRuleException(
                StaffAdminRules.UnknownRole, "Atanan rol panelin rollerinden değil ya da artık yok.");
        }

        return [.. roleIds.Distinct().Select(id => staffRoles[id])];
    }

    private async Task<IReadOnlyList<DirectoryRole>> StaffRolesOfAsync(Guid staffId, CancellationToken ct)
    {
        var assigned = await directory.UserRoleIdsAsync(staffId, ct);

        return
        [
            .. (await directory.ListRolesAsync(ct))
                .Where(r => r.Kind is DirectoryRoleKind.StaffRole && assigned.Contains(r.Id))
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    private static void EnsureNotSelf(Actor actor, Guid staffId)
    {
        if (actor.Id == staffId)
        {
            throw new StaffAdminRuleException(
                StaffAdminRules.OwnAccount, "Kendi rollerini değiştiremez ve kendini kapatamazsın; başka bir yönetici yapmalı.");
        }
    }
}

/// <param name="Roles">Panelin rolleri; izinler ve kimlik sağlayıcının rolleri hariç.</param>
public sealed record StaffDetail(DirectoryUser User, IReadOnlyList<DirectoryRole> Roles);
