namespace HiWallet.StaffAdmin.Domain;

/// <summary>Kaydın veritabanındaki ve API'deki metin değerleri; ikisi aynı.</summary>
public static class StaffAuditTexts
{
    public static readonly IReadOnlyDictionary<StaffAuditAction, string> Actions = new Dictionary<StaffAuditAction, string>
    {
        [StaffAuditAction.RoleCreated] = "role_created",
        [StaffAuditAction.RoleUpdated] = "role_updated",
        [StaffAuditAction.RoleDeleted] = "role_deleted",
        [StaffAuditAction.StaffInvited] = "staff_invited",
        [StaffAuditAction.StaffRolesChanged] = "staff_roles_changed",
        [StaffAuditAction.StaffDisabled] = "staff_disabled",
        [StaffAuditAction.StaffEnabled] = "staff_enabled",
        [StaffAuditAction.InvitationSent] = "invitation_sent"
    };

    public static readonly IReadOnlyDictionary<StaffAuditTarget, string> Targets = new Dictionary<StaffAuditTarget, string>
    {
        [StaffAuditTarget.Role] = "role",
        [StaffAuditTarget.Staff] = "staff"
    };
}
