using System.Text.Json;

namespace HiWallet.StaffAdmin.Api;

public sealed record PermissionResponse(string Name, string Description);

public sealed record RoleResponse(Guid RoleId, string Name, string? Description, IReadOnlyList<string> Permissions);

/// <param name="NextFirst">Bir sonraki sayfanın <c>first</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record RolesResponse(IReadOnlyList<RoleResponse> Items, int First, int Size, int? NextFirst);

/// <param name="Members">Role doğrudan atanmış çalışanlar.</param>
public sealed record RoleDetailResponse(
    Guid RoleId,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<StaffSummaryResponse> Members);

/// <param name="InvitationPending">Çalışan parolasını ya da OTP'sini henüz kurmadı.</param>
public sealed record StaffSummaryResponse(
    Guid StaffId,
    string Email,
    string? FirstName,
    string? LastName,
    bool Enabled,
    bool InvitationPending,
    DateTimeOffset CreatedAt);

/// <param name="NextFirst">Bir sonraki sayfanın <c>first</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record StaffPageResponse(IReadOnlyList<StaffSummaryResponse> Items, int First, int Size, int? NextFirst);

public sealed record StaffRoleRef(Guid RoleId, string Name);

public sealed record StaffDetailResponse(
    Guid StaffId,
    string Email,
    string? FirstName,
    string? LastName,
    bool Enabled,
    bool InvitationPending,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StaffRoleRef> Roles);

/// <param name="Action">
/// <c>role_created</c>, <c>role_updated</c>, <c>role_deleted</c>, <c>staff_invited</c>,
/// <c>staff_roles_changed</c>, <c>staff_disabled</c>, <c>staff_enabled</c> ya da
/// <c>invitation_sent</c>.
/// </param>
/// <param name="TargetType"><c>role</c> ya da <c>staff</c>.</param>
/// <param name="Details">Değişikliğin ayrıntısı: verilen izinler, önceki ve sonraki roller.</param>
public sealed record AuditEventResponse(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string ActorSubject,
    string? ActorName,
    string Action,
    string TargetType,
    Guid TargetId,
    string TargetLabel,
    JsonElement Details);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record AuditEventsResponse(IReadOnlyList<AuditEventResponse> Items, int Size, Guid? NextCursor);
