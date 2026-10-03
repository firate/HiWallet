using System.Text.Json;

namespace HiWallet.EdgeApi.Contracts;

/// <summary>Çalışanın kendisi: şu anki rolleri ve izinleri.</summary>
/// <param name="Roles">Rollerin adları. Kapatılmış ya da panelden açılmamış çalışanda boş.</param>
/// <param name="Permissions">Rollerden gelen izinler, kodun sırasıyla.</param>
public sealed record MeResponse(string Subject, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

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

/// <param name="Name">Sonradan değişmiyor.</param>
public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(string? Description, IReadOnlyList<string> Permissions);

/// <param name="InvitationPending">Çalışan davetini tamamlayıp henüz giriş yapmadı.</param>
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

public sealed record InviteStaffRequest(string Email, string? FirstName, string? LastName, IReadOnlyList<Guid> RoleIds);

/// <param name="RoleIds">Çalışanın rollerinin tamamı; listede olmayanlar alınıyor.</param>
public sealed record SetStaffRolesRequest(IReadOnlyList<Guid> RoleIds);

/// <param name="Details">Değişikliğin ayrıntısı; şekli işleme göre değişiyor.</param>
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
