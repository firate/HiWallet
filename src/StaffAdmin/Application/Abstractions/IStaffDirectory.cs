namespace HiWallet.StaffAdmin.Application.Abstractions;

/// <summary>
/// Çalışanların kimlik sağlayıcısındaki kayıtlar: kullanıcılar, roller ve rollerin
/// içerdiği izinler. Kaynak doğru orası; token'ı o yazıyor. Bu servis kayıtları
/// değiştiriyor ve kimin değiştirdiğini kendi veritabanına yazıyor.
///
/// İzin ve rol ikisi de kimlik sağlayıcıda realm rolü: izin kodun tanıdığı sabit bir rol,
/// rol izinleri içeren bileşik bir rol. Hangisi olduğu rolün işaretinde
/// (<see cref="DirectoryRoleKind"/>).
/// </summary>
public interface IStaffDirectory
{
    /// <summary>Realm'in bütün rolleri; izinler, panelin rolleri ve kimlik sağlayıcının kendi rolleri.</summary>
    Task<IReadOnlyList<DirectoryRole>> ListRolesAsync(CancellationToken ct);

    Task<DirectoryRole?> FindRoleAsync(Guid roleId, CancellationToken ct);

    /// <summary>Panelin rolü: verilen izinleri içeren bileşik rol.</summary>
    /// <exception cref="DirectoryConflictException">Aynı adda bir rol var.</exception>
    Task<Guid> CreateRoleAsync(string name, string description, IReadOnlyCollection<string> permissions, CancellationToken ct);

    /// <summary>Açıklamayı ve izinleri değiştirir; ad değişmiyor.</summary>
    Task UpdateRoleAsync(Guid roleId, string description, IReadOnlyCollection<string> permissions, CancellationToken ct);

    /// <summary>Rolü siler; kullanıcılardaki atamaları da gidiyor.</summary>
    Task DeleteRoleAsync(Guid roleId, CancellationToken ct);

    /// <summary>İzin yoksa açar; varsa dokunmaz.</summary>
    Task EnsurePermissionAsync(string permission, string description, CancellationToken ct);

    /// <summary>Role DOĞRUDAN atanmış kullanıcılar.</summary>
    Task<IReadOnlyList<DirectoryUser>> RoleMembersAsync(Guid roleId, CancellationToken ct);

    /// <param name="first">Atlanacak kayıt sayısı; kimlik sağlayıcı yalnızca sıra numarasıyla sayfalıyor.</param>
    Task<DirectoryUserPage> ListUsersAsync(int first, int size, string? search, CancellationToken ct);

    Task<DirectoryUser?> FindUserAsync(Guid userId, CancellationToken ct);

    Task<DirectoryUser?> FindUserByEmailAsync(string email, CancellationToken ct);

    /// <summary>Kullanıcıyı açar; parolası ve OTP'si yok, onları davetle kendisi kuruyor.</summary>
    /// <exception cref="DirectoryConflictException">Bu e-postayla bir kullanıcı var.</exception>
    Task<Guid> CreateUserAsync(string email, string? firstName, string? lastName, CancellationToken ct);

    /// <summary>Parolayı ve OTP'yi kurdurdan bağlantıyı e-postayla gönderir.</summary>
    Task SendInvitationAsync(Guid userId, CancellationToken ct);

    /// <summary>Kullanıcıya doğrudan atanmış rollerin kimlikleri.</summary>
    Task<IReadOnlyList<Guid>> UserRoleIdsAsync(Guid userId, CancellationToken ct);

    Task AssignRolesAsync(Guid userId, IReadOnlyCollection<DirectoryRole> roles, CancellationToken ct);

    Task UnassignRolesAsync(Guid userId, IReadOnlyCollection<DirectoryRole> roles, CancellationToken ct);

    /// <summary>Kapatılan kullanıcının açık oturumları da kapanıyor.</summary>
    Task SetEnabledAsync(Guid userId, bool enabled, CancellationToken ct);
}

public enum DirectoryRoleKind
{
    /// <summary>Kodun tanıdığı izin.</summary>
    Permission,

    /// <summary>Panelde tanımlanmış, izinleri içeren rol.</summary>
    StaffRole,

    /// <summary>Kimlik sağlayıcının kendi rolleri (varsayılan roller gibi); panel bunlara dokunmuyor.</summary>
    Other
}

/// <param name="Permissions">Panelin rolünde içerdiği izinler; diğer türlerde boş.</param>
public sealed record DirectoryRole(
    Guid Id,
    string Name,
    string? Description,
    DirectoryRoleKind Kind,
    IReadOnlyList<string> Permissions);

/// <param name="PendingActions">Kullanıcının girişte yapması gereken işlemler; davet bekleyen çalışanda dolu.</param>
public sealed record DirectoryUser(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    bool Enabled,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> PendingActions);

/// <param name="HasMore">Bu sayfadan sonra kayıt var mı.</param>
public sealed record DirectoryUserPage(IReadOnlyList<DirectoryUser> Items, bool HasMore);

/// <summary>Kimlik sağlayıcıda aynı adda rol ya da aynı e-postada kullanıcı var.</summary>
public sealed class DirectoryConflictException(string message) : Exception(message);
