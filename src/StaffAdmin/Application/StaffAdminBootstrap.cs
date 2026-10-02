using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Application.Abstractions;
using HiWallet.StaffAdmin.Domain;
using Microsoft.Extensions.Options;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Açılışta, kimlik sağlayıcıya ulaşana kadar yeniden deneyerek:
/// <list type="number">
/// <item>Kodun izinleri realm'de rol olarak açılıyor. Yeni bir izin kodla geliyor, panelde
/// kendiliğinden seçilebiliyor.</item>
/// <item>Yalnızca personel yönetimi iznini içeren yönetici rolü yoksa açılıyor.</item>
/// <item>Hiçbir etkin çalışanda personel yönetimi yoksa ayardaki adrese davet gidiyor ve
/// yönetici rolü veriliyor. Bütün yöneticiler kaybedilirse kurtarma yolu da bu.</item>
/// </list>
/// Her açılışta aynı sonucu veriyor; var olana dokunmuyor.
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
                await RunAsync(
                    scope.ServiceProvider.GetRequiredService<IStaffDirectory>(),
                    scope.ServiceProvider.GetRequiredService<StaffAudit>(),
                    stoppingToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRetry(exception, RetryDelay);
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    private async Task RunAsync(IStaffDirectory directory, StaffAudit audit, CancellationToken ct)
    {
        foreach (var (permission, description) in StaffPermissions.Descriptions)
        {
            await directory.EnsurePermissionAsync(permission, description, ct);
        }

        var settings = options.Value.Bootstrap;
        var adminRole = (await directory.ListRolesAsync(ct))
            .FirstOrDefault(r => r.Kind is DirectoryRoleKind.StaffRole && r.Name == settings.AdminRoleName);

        if (adminRole is null)
        {
            IReadOnlyList<string> permissions = [StaffPermissions.StaffManage];
            var roleId = await directory.CreateRoleAsync(
                settings.AdminRoleName, StaffPermissions.Descriptions[StaffPermissions.StaffManage], permissions, ct);
            adminRole = new DirectoryRole(roleId, settings.AdminRoleName, null, DirectoryRoleKind.StaffRole, permissions);

            await audit.RecordAsync(
                Actor.System, StaffAuditAction.RoleCreated, StaffAuditTarget.Role, roleId, adminRole.Name,
                new { permissions }, ct);
        }

        if (string.IsNullOrWhiteSpace(settings.AdminEmail) || await AnyEnabledManagerAsync(directory, ct))
        {
            return;
        }

        var existing = await directory.FindUserByEmailAsync(settings.AdminEmail, ct);
        var adminId = existing?.Id ?? await directory.CreateUserAsync(settings.AdminEmail, null, null, ct);

        if (existing is { Enabled: false })
        {
            await directory.SetEnabledAsync(adminId, true, ct);
        }

        await directory.AssignRolesAsync(adminId, [adminRole], ct);
        await directory.SendInvitationAsync(adminId, ct);

        await audit.RecordAsync(
            Actor.System, StaffAuditAction.StaffInvited, StaffAuditTarget.Staff, adminId, settings.AdminEmail,
            new { roles = new[] { adminRole.Name } }, ct);

        LogAdminInvited(settings.AdminEmail);
    }

    private static async Task<bool> AnyEnabledManagerAsync(IStaffDirectory directory, CancellationToken ct)
    {
        var managerRoles = (await directory.ListRolesAsync(ct))
            .Where(r => r.Kind is DirectoryRoleKind.StaffRole && r.Permissions.Contains(StaffPermissions.StaffManage));

        foreach (var role in managerRoles)
        {
            if ((await directory.RoleMembersAsync(role.Id, ct)).Any(u => u.Enabled))
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Personel yönetiminin açılış kurulumu yapılamadı; {Delay} sonra yeniden denenecek.")]
    private partial void LogRetry(Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Hiçbir çalışanda personel yönetimi yoktu; ilk yönetici olarak {Email} adresine davet gönderildi.")]
    private partial void LogAdminInvited(string email);
}
