using HiWallet.StaffAdmin.Application.Abstractions;
using HiWallet.StaffAdmin.Domain;
using HiWallet.StaffAdmin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Çalışanlar: davet, rol atama, kapatma. Çalışan yetkiyi yalnızca panelin rollerinden
/// alıyor. Çalışan kendi rollerini değiştiremiyor ve kendini kapatamıyor.
///
/// Kimlik sağlayıcıya giden adımla veritabanı arasında ortak transaction yok. Sıra, yarım
/// kalan işin güvenli tarafta kalacağı şekilde: davette kullanıcı ve e-posta önce, kayıt
/// sonra (kayıt yazılamazsa çalışanın izni yok, tekrar aynı kullanıcıyı buluyor);
/// kapatmada kayıt önce (izin hemen gidiyor), kimlik sağlayıcı sonra.
/// </summary>
public sealed class StaffService(
    IDbContextFactory<StaffAdminDbContext> contexts,
    IStaffDirectory directory,
    StaffAudit audit,
    TimeProvider time)
{
    public async Task<(IReadOnlyList<StaffMember> Items, bool HasMore)> ListAsync(
        int skip, int take, string? search, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var query = db.Members.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(m =>
                EF.Functions.ILike(m.Email, pattern)
                || (m.FirstName != null && EF.Functions.ILike(m.FirstName, pattern))
                || (m.LastName != null && EF.Functions.ILike(m.LastName, pattern)));
        }

        var rows = await query.OrderBy(m => m.Email).Skip(skip).Take(take + 1).ToListAsync(ct);

        return ([.. rows.Take(take)], rows.Count > take);
    }

    public async Task<StaffDetail> GetAsync(Guid staffId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        return new StaffDetail(await RequireAsync(db, staffId, ct), await RolesOfAsync(db, staffId, ct));
    }

    /// <summary>
    /// Kullanıcıyı açar, daveti gönderir, çalışanı ve rollerini kaydeder. Parola ve OTP'yi
    /// çalışan davetteki bağlantıdan kendisi kuruyor; panel parolayı hiç görmüyor.
    ///
    /// Kimlik sağlayıcıda aynı e-postada kullanıcı varsa (önceki deneme kaydı yazamadan
    /// kaldıysa) o kullanıcı kullanılıyor.
    /// </summary>
    public async Task<StaffDetail> InviteAsync(
        Actor actor, string email, string? firstName, string? lastName, IReadOnlyCollection<Guid> roleIds,
        CancellationToken ct)
    {
        email = StaffMember.NormalizeEmail(email);
        await using var db = await contexts.CreateDbContextAsync(ct);
        var roles = await ResolveRolesAsync(db, roleIds, ct);

        if (await db.Members.AnyAsync(m => m.Email == email, ct))
        {
            throw new StaffAdminConflictException(StaffAdminRules.StaffExists, "Bu e-postayla bir çalışan var.");
        }

        var staffId = await EnsureDirectoryUserAsync(directory, email, firstName, lastName, ct);
        await directory.SendInvitationAsync(staffId, ct);

        var member = StaffMember.Invite(staffId, email, firstName, lastName, time.GetUtcNow());
        db.Members.Add(member);
        db.RoleAssignments.AddRange(roles.Select(r => StaffRoleAssignment.For(staffId, r.Id)));

        audit.Add(db, actor, StaffAuditAction.StaffInvited, StaffAuditTarget.Staff, staffId, email,
            new { roles = roles.Select(r => r.Name) });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (UniqueViolation.Is(exception))
        {
            throw new StaffAdminConflictException(StaffAdminRules.StaffExists, "Bu e-postayla bir çalışan var.");
        }

        return new StaffDetail(member, roles);
    }

    /// <summary>Çalışanın rollerinin tamamı; listede olmayanlar alınıyor.</summary>
    public async Task<StaffDetail> SetRolesAsync(
        Actor actor, Guid staffId, IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        EnsureNotSelf(actor, staffId);
        await using var db = await contexts.CreateDbContextAsync(ct);
        var member = await RequireAsync(db, staffId, ct);
        var desired = await ResolveRolesAsync(db, roleIds, ct);
        var current = await RolesOfAsync(db, staffId, ct);

        var added = desired.ExceptBy(current.Select(r => r.Id), r => r.Id).ToList();
        var removed = current.ExceptBy(desired.Select(r => r.Id), r => r.Id).ToList();

        if (added.Count == 0 && removed.Count == 0)
        {
            return new StaffDetail(member, current);
        }

        var removedIds = removed.Select(r => r.Id).ToList();

        db.RoleAssignments.AddRange(added.Select(r => StaffRoleAssignment.For(staffId, r.Id)));
        db.RoleAssignments.RemoveRange(await db.RoleAssignments
            .Where(a => a.StaffId == staffId && removedIds.Contains(a.RoleId))
            .ToListAsync(ct));

        audit.Add(db, actor, StaffAuditAction.StaffRolesChanged, StaffAuditTarget.Staff, staffId, member.Email,
            new { before = current.Select(r => r.Name), after = desired.Select(r => r.Name) });

        await db.SaveChangesAsync(ct);

        return new StaffDetail(member, desired);
    }

    /// <summary>
    /// Kapatılan çalışanın izni kayıt yazıldığı an gidiyor; kimlik sağlayıcıda girişi ve
    /// açık oturumları da kapanıyor. Kimlik sağlayıcıya ulaşılamazsa istek hata dönüyor ve
    /// tekrarı yalnızca o adımı yapıyor.
    /// </summary>
    public async Task<StaffDetail> SetEnabledAsync(Actor actor, Guid staffId, bool enabled, CancellationToken ct)
    {
        EnsureNotSelf(actor, staffId);
        await using var db = await contexts.CreateDbContextAsync(ct);
        var member = await RequireAsync(db, staffId, ct);

        if (member.Enabled != enabled)
        {
            member.SetEnabled(enabled);

            audit.Add(db, actor, enabled ? StaffAuditAction.StaffEnabled : StaffAuditAction.StaffDisabled,
                StaffAuditTarget.Staff, staffId, member.Email, new { });

            await db.SaveChangesAsync(ct);
        }

        await directory.SetEnabledAsync(staffId, enabled, ct);

        return new StaffDetail(member, await RolesOfAsync(db, staffId, ct));
    }

    public async Task ResendInvitationAsync(Actor actor, Guid staffId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var member = await RequireAsync(db, staffId, ct);

        await directory.SendInvitationAsync(staffId, ct);

        audit.Add(db, actor, StaffAuditAction.InvitationSent, StaffAuditTarget.Staff, staffId, member.Email, new { });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Kimlik sağlayıcıdaki kullanıcı: varsa o (kapalıysa açılıyor), yoksa yeni açılan.</summary>
    internal static async Task<Guid> EnsureDirectoryUserAsync(
        IStaffDirectory directory, string email, string? firstName, string? lastName, CancellationToken ct)
    {
        var existing = await directory.FindUserByEmailAsync(email, ct);

        if (existing is null)
        {
            try
            {
                return await directory.CreateUserAsync(email, firstName, lastName, ct);
            }
            catch (DirectoryConflictException)
            {
                // Arada başka bir istek açtı.
                existing = await directory.FindUserByEmailAsync(email, ct)
                           ?? throw new InvalidOperationException($"{email} kimlik sağlayıcıda var ama bulunamadı.");
            }
        }

        if (!existing.Enabled)
        {
            await directory.SetEnabledAsync(existing.Id, true, ct);
        }

        return existing.Id;
    }

    private static async Task<StaffMember> RequireAsync(StaffAdminDbContext db, Guid staffId, CancellationToken ct) =>
        await db.Members.SingleOrDefaultAsync(m => m.Id == staffId, ct)
        ?? throw new StaffAdminNotFoundException("Çalışan bulunamadı.");

    private static async Task<IReadOnlyList<StaffRole>> ResolveRolesAsync(
        StaffAdminDbContext db, IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var ids = roleIds.Distinct().ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(ct);

        if (roles.Count != ids.Count)
        {
            throw new StaffAdminRuleException(StaffAdminRules.UnknownRole, "Atanan rol yok ya da silinmiş.");
        }

        return [.. roles.OrderBy(r => r.NormalizedName, StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<StaffRole>> RolesOfAsync(
        StaffAdminDbContext db, Guid staffId, CancellationToken ct) =>
        await db.Roles.AsNoTracking()
            .Where(r => db.RoleAssignments.Any(a => a.StaffId == staffId && a.RoleId == r.Id))
            .OrderBy(r => r.NormalizedName)
            .ToListAsync(ct);

    private static void EnsureNotSelf(Actor actor, Guid staffId)
    {
        if (actor.Id == staffId)
        {
            throw new StaffAdminRuleException(
                StaffAdminRules.OwnAccount, "Kendi rollerini değiştiremez ve kendini kapatamazsın; başka bir yönetici yapmalı.");
        }
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}

public sealed record StaffDetail(StaffMember Member, IReadOnlyList<StaffRole> Roles);
