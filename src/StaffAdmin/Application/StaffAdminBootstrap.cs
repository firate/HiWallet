using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Application.Abstractions;
using HiWallet.StaffAdmin.Domain;
using HiWallet.StaffAdmin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Açılışta, veritabanına ve kimlik sağlayıcıya ulaşana kadar yeniden deneyerek:
/// <list type="number">
/// <item>Yalnızca personel yönetimi iznini içeren yönetici rolü yoksa açılıyor.</item>
/// <item>Hiçbir etkin çalışanda personel yönetimi yoksa ayardaki adrese davet gidiyor ve
/// yönetici rolü veriliyor. Bütün yöneticiler kaybedilirse kurtarma yolu da bu: servis
/// yeniden başlatılıyor.</item>
/// </list>
/// Her açılışta aynı sonucu veriyor; var olana dokunmuyor. Davet kayıttan ÖNCE gidiyor:
/// kayıt yazılamazsa sonraki deneme her şeyi baştan yapıyor ve aynı kullanıcıyı buluyor.
/// </summary>
public sealed partial class StaffAdminBootstrap(
    IServiceScopeFactory scopes,
    IOptions<StaffAdminOptions> options,
    ILogger<StaffAdminBootstrap> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await RunAsync(scope.ServiceProvider, stoppingToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRetry(exception, RetryDelay);
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    private async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var settings = options.Value.Bootstrap;
        var directory = services.GetRequiredService<IStaffDirectory>();
        var audit = services.GetRequiredService<StaffAudit>();
        var time = services.GetRequiredService<TimeProvider>();
        await using var db = await services.GetRequiredService<IDbContextFactory<StaffAdminDbContext>>()
            .CreateDbContextAsync(ct);

        var adminRole = await EnsureAdminRoleAsync(db, audit, time, settings.AdminRoleName, ct);

        if (string.IsNullOrWhiteSpace(settings.AdminEmail) || await AnyEnabledManagerAsync(db, ct))
        {
            return;
        }

        // Yönetici rolünün izni elle alınmışsa kurtarma onu geri veriyor; yoksa davet edilen
        // yönetici de personeli yönetemezdi.
        if (!adminRole.Permissions.Contains(StaffPermissions.StaffManage))
        {
            var before = new { adminRole.Description, Permissions = adminRole.Permissions.ToList() };
            adminRole.Change(adminRole.Description, [.. adminRole.Permissions, StaffPermissions.StaffManage]);

            audit.Add(db, Actor.System, StaffAuditAction.RoleUpdated, StaffAuditTarget.Role, adminRole.Id, adminRole.Name,
                new { before, after = new { adminRole.Description, adminRole.Permissions } });
        }

        var email = StaffMember.NormalizeEmail(settings.AdminEmail);
        var member = await db.Members.SingleOrDefaultAsync(m => m.Email == email, ct);
        var staffId = member?.Id ?? await StaffService.EnsureDirectoryUserAsync(directory, email, null, null, ct);

        if (member is { Enabled: false })
        {
            await directory.SetEnabledAsync(staffId, true, ct);
            member.SetEnabled(true);
        }

        await directory.SendInvitationAsync(staffId, ct);

        if (member is null)
        {
            db.Members.Add(StaffMember.Invite(staffId, email, null, null, time.GetUtcNow()));
        }

        if (!await db.RoleAssignments.AnyAsync(a => a.StaffId == staffId && a.RoleId == adminRole.Id, ct))
        {
            db.RoleAssignments.Add(StaffRoleAssignment.For(staffId, adminRole.Id));
        }

        audit.Add(db, Actor.System, StaffAuditAction.StaffInvited, StaffAuditTarget.Staff, staffId, email,
            new { roles = new[] { adminRole.Name } });

        await db.SaveChangesAsync(ct);

        LogAdminInvited(email);
    }

    private static async Task<StaffRole> EnsureAdminRoleAsync(
        StaffAdminDbContext db, StaffAudit audit, TimeProvider time, string name, CancellationToken ct)
    {
        var normalized = StaffRole.NormalizeName(name);

        if (await db.Roles.SingleOrDefaultAsync(r => r.NormalizedName == normalized, ct) is { } existing)
        {
            return existing;
        }

        IReadOnlyList<string> permissions = [StaffPermissions.StaffManage];
        var role = StaffRole.Create(name, StaffPermissions.Descriptions[StaffPermissions.StaffManage], permissions, time.GetUtcNow());
        db.Roles.Add(role);

        audit.Add(db, Actor.System, StaffAuditAction.RoleCreated, StaffAuditTarget.Role, role.Id, role.Name,
            new { role.Description, permissions = role.Permissions });

        await db.SaveChangesAsync(ct);

        return role;
    }

    private static Task<bool> AnyEnabledManagerAsync(StaffAdminDbContext db, CancellationToken ct) =>
        db.Members.AnyAsync(m => m.Enabled && db.RoleAssignments.Any(a =>
            a.StaffId == m.Id
            && db.Roles.Any(r => r.Id == a.RoleId && r.Permissions.Contains(StaffPermissions.StaffManage))), ct);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Personel yönetiminin açılış kurulumu yapılamadı; {Delay} sonra yeniden denenecek.")]
    private partial void LogRetry(Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Hiçbir çalışanda personel yönetimi yoktu; ilk yönetici olarak {Email} adresine davet gönderildi.")]
    private partial void LogAdminInvited(string email);
}
