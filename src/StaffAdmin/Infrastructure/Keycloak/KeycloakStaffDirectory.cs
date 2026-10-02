using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HiWallet.StaffAdmin.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace HiWallet.StaffAdmin.Infrastructure.Keycloak;

/// <summary>
/// Keycloak'ın yönetim API'si, çalışanların realm'inin adresine göre
/// (<c>admin/realms/{realm}/</c>). İzin ve panelin rolü ikisi de realm rolü; türü rolün
/// <c>hiwallet.kind</c> özelliğinde. Panelin rolü izinleri içeren bileşik rol: token'a
/// içindeki izinler açılmış olarak yazılıyor.
/// </summary>
internal sealed class KeycloakStaffDirectory(HttpClient http, IOptions<KeycloakOptions> options) : IStaffDirectory
{
    private const string KindAttribute = "hiwallet.kind";
    private const string PermissionKind = "permission";
    private const string StaffRoleKind = "role";

    /// <summary>
    /// Realm'deki rol sayısı küçük ve sınırlı: kodun izinleri, panelin rolleri ve
    /// Keycloak'ın birkaç kendi rolü. Tavan aşılırsa liste sessizce kesilmesin diye hata.
    /// </summary>
    private const int RoleLimit = 500;

    /// <summary>Bir rolün üyeleri için aynı gerekçe.</summary>
    private const int MemberLimit = 1000;

    private const string ServiceAccountPrefix = "service-account-";

    public async Task<IReadOnlyList<DirectoryRole>> ListRolesAsync(CancellationToken ct)
    {
        var roles = await http.GetFromJsonAsync<RoleRepresentation[]>(
            $"roles?briefRepresentation=false&first=0&max={RoleLimit + 1}", ct) ?? [];

        if (roles.Length > RoleLimit)
        {
            throw new InvalidOperationException($"Realm'de {RoleLimit}'den fazla rol var; liste kesilirdi.");
        }

        var result = new List<DirectoryRole>(roles.Length);

        foreach (var role in roles)
        {
            result.Add(await ToDirectoryRoleAsync(role, ct));
        }

        return result;
    }

    public async Task<DirectoryRole?> FindRoleAsync(Guid roleId, CancellationToken ct) =>
        await FindRoleRepresentationAsync(roleId, ct) is { } role ? await ToDirectoryRoleAsync(role, ct) : null;

    public async Task<Guid> CreateRoleAsync(
        string name, string description, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        using (var response = await http.PostAsJsonAsync("roles", new
               {
                   name,
                   description,
                   attributes = new Dictionary<string, string[]> { [KindAttribute] = [StaffRoleKind] }
               }, ct))
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new DirectoryConflictException($"{name} adında bir rol var.");
            }

            response.EnsureSuccessStatusCode();
        }

        // Keycloak açılan rolün kimliğini dönmüyor; adıyla okunuyor.
        var created = await http.GetFromJsonAsync<RoleRepresentation>($"roles/{Uri.EscapeDataString(name)}", ct)
                      ?? throw new InvalidOperationException("Açılan rol okunamadı.");

        await ChangeCompositesAsync(created.Id, HttpMethod.Post, await RoleRefsAsync(permissions, ct), ct);

        return Guid.Parse(created.Id);
    }

    public async Task UpdateRoleAsync(
        Guid roleId, string description, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        // Tam gösterim okunup geri yazılıyor: gönderilmeyen alanlar silinmesin.
        var role = await http.GetFromJsonAsync<JsonObject>($"roles-by-id/{roleId}", ct)
                   ?? throw new InvalidOperationException("Rol okunamadı.");
        role["description"] = description;

        using (var response = await http.PutAsJsonAsync($"roles-by-id/{roleId}", role, ct))
        {
            response.EnsureSuccessStatusCode();
        }

        var current = await CompositesAsync(roleId.ToString(), ct);
        var desired = await RoleRefsAsync(permissions, ct);

        await ChangeCompositesAsync(
            roleId.ToString(), HttpMethod.Post, [.. desired.ExceptBy(current.Select(c => c.Name), r => r.Name)], ct);
        await ChangeCompositesAsync(
            roleId.ToString(), HttpMethod.Delete, [.. current.ExceptBy(desired.Select(d => d.Name), c => c.Name)], ct);
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct)
    {
        using var response = await http.DeleteAsync($"roles-by-id/{roleId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task EnsurePermissionAsync(string permission, string description, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("roles", new
        {
            name = permission,
            description,
            attributes = new Dictionary<string, string[]> { [KindAttribute] = [PermissionKind] }
        }, ct);

        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
            return;
        }

        // Aynı adda rol var. Elle açılmışsa işareti yok ve izin sayılmıyordu; işaretleniyor.
        var existing = await http.GetFromJsonAsync<JsonObject>($"roles/{Uri.EscapeDataString(permission)}", ct)
                       ?? throw new InvalidOperationException("İzin okunamadı.");

        if (KindOf(existing["attributes"]?.Deserialize<Dictionary<string, string[]>>()) == DirectoryRoleKind.Permission)
        {
            return;
        }

        var attributes = existing["attributes"] as JsonObject ?? [];
        attributes[KindAttribute] = new JsonArray(PermissionKind);
        existing["attributes"] = attributes;

        using var update = await http.PutAsJsonAsync($"roles-by-id/{existing["id"]!.GetValue<string>()}", existing, ct);
        update.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DirectoryUser>> RoleMembersAsync(Guid roleId, CancellationToken ct)
    {
        var role = await FindRoleRepresentationAsync(roleId, ct);

        if (role is null)
        {
            return [];
        }

        var users = await http.GetFromJsonAsync<UserRepresentation[]>(
            $"roles/{Uri.EscapeDataString(role.Name)}/users?first=0&max={MemberLimit + 1}", ct) ?? [];

        if (users.Length > MemberLimit)
        {
            throw new InvalidOperationException($"Rolün {MemberLimit}'den fazla üyesi var; liste kesilirdi.");
        }

        return [.. users.Where(IsStaff).Select(ToDirectoryUser)];
    }

    public async Task<DirectoryUserPage> ListUsersAsync(int first, int size, string? search, CancellationToken ct)
    {
        var query = $"users?briefRepresentation=false&first={first}&max={size + 1}";

        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search)}";
        }

        var users = await http.GetFromJsonAsync<UserRepresentation[]>(query, ct) ?? [];

        return new DirectoryUserPage([.. users.Take(size).Where(IsStaff).Select(ToDirectoryUser)], users.Length > size);
    }

    public async Task<DirectoryUser?> FindUserAsync(Guid userId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"users/{userId}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserRepresentation>(ct);

        return user is not null && IsStaff(user) ? ToDirectoryUser(user) : null;
    }

    public async Task<DirectoryUser?> FindUserByEmailAsync(string email, CancellationToken ct)
    {
        var users = await http.GetFromJsonAsync<UserRepresentation[]>(
            $"users?briefRepresentation=false&exact=true&email={Uri.EscapeDataString(email)}", ct) ?? [];

        return users.Where(IsStaff).Select(ToDirectoryUser).FirstOrDefault();
    }

    public async Task<Guid> CreateUserAsync(string email, string? firstName, string? lastName, CancellationToken ct)
    {
        // E-postayı yönetici yazdı; davet o adrese gidiyor ve çalışan bağlantıyla kendisi kanıtlıyor.
        using var response = await http.PostAsJsonAsync("users", new
        {
            username = email,
            email,
            emailVerified = true,
            enabled = true,
            firstName,
            lastName
        }, ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new DirectoryConflictException($"{email} kayıtlı.");
        }

        response.EnsureSuccessStatusCode();

        // Kullanıcının kimliği gövdede değil, Location'ın son parçasında.
        var id = response.Headers.Location?.Segments.LastOrDefault()?.TrimEnd('/')
                 ?? throw new InvalidOperationException("Kimlik sağlayıcı açılan kullanıcının adresini dönmedi.");

        return Guid.Parse(id);
    }

    public async Task SendInvitationAsync(Guid userId, CancellationToken ct)
    {
        var invitation = options.Value.Invitation;
        var query = $"lifespan={TimeSpan.FromHours(invitation.LifespanHours).TotalSeconds.ToString(CultureInfo.InvariantCulture)}";

        if (!string.IsNullOrWhiteSpace(invitation.ClientId) && !string.IsNullOrWhiteSpace(invitation.RedirectUri))
        {
            query += $"&client_id={Uri.EscapeDataString(invitation.ClientId)}" +
                     $"&redirect_uri={Uri.EscapeDataString(invitation.RedirectUri)}";
        }

        using var response = await http.PutAsJsonAsync(
            $"users/{userId}/execute-actions-email?{query}", new[] { "UPDATE_PASSWORD", "CONFIGURE_TOTP" }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<Guid>> UserRoleIdsAsync(Guid userId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"users/{userId}/role-mappings/realm", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var roles = await response.Content.ReadFromJsonAsync<RoleRepresentation[]>(ct) ?? [];

        return [.. roles.Select(r => Guid.Parse(r.Id))];
    }

    public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<DirectoryRole> roles, CancellationToken ct) =>
        SendRolesAsync(HttpMethod.Post, $"users/{userId}/role-mappings/realm", roles.Select(Ref).ToList(), ct);

    public Task UnassignRolesAsync(Guid userId, IReadOnlyCollection<DirectoryRole> roles, CancellationToken ct) =>
        SendRolesAsync(HttpMethod.Delete, $"users/{userId}/role-mappings/realm", roles.Select(Ref).ToList(), ct);

    public async Task SetEnabledAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        // Tam gösterim okunup geri yazılıyor: gönderilmeyen alanlar silinmesin.
        var user = await http.GetFromJsonAsync<JsonObject>($"users/{userId}", ct)
                   ?? throw new InvalidOperationException("Kullanıcı okunamadı.");
        user["enabled"] = enabled;

        using (var response = await http.PutAsJsonAsync($"users/{userId}", user, ct))
        {
            response.EnsureSuccessStatusCode();
        }

        if (!enabled)
        {
            using var logout = await http.PostAsync($"users/{userId}/logout", null, ct);
            logout.EnsureSuccessStatusCode();
        }
    }

    private async Task<RoleRepresentation?> FindRoleRepresentationAsync(Guid roleId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"roles-by-id/{roleId}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RoleRepresentation>(ct);
    }

    private async Task<DirectoryRole> ToDirectoryRoleAsync(RoleRepresentation role, CancellationToken ct)
    {
        var kind = KindOf(role.Attributes);
        IReadOnlyList<string> permissions = kind is DirectoryRoleKind.StaffRole
            ? [.. (await CompositesAsync(role.Id, ct)).Select(c => c.Name)]
            : [];

        return new DirectoryRole(Guid.Parse(role.Id), role.Name, role.Description, kind, permissions);
    }

    private async Task<IReadOnlyList<RoleRef>> CompositesAsync(string roleId, CancellationToken ct) =>
    [
        .. (await http.GetFromJsonAsync<RoleRepresentation[]>($"roles-by-id/{roleId}/composites/realm", ct) ?? [])
            .Select(r => new RoleRef(r.Id, r.Name))
    ];

    private async Task<IReadOnlyList<RoleRef>> RoleRefsAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var refs = new List<RoleRef>();

        foreach (var name in names)
        {
            var role = await http.GetFromJsonAsync<RoleRepresentation>($"roles/{Uri.EscapeDataString(name)}", ct)
                       ?? throw new InvalidOperationException($"{name} izni realm'de yok.");
            refs.Add(new RoleRef(role.Id, role.Name));
        }

        return refs;
    }

    private Task ChangeCompositesAsync(string roleId, HttpMethod method, IReadOnlyList<RoleRef> roles, CancellationToken ct) =>
        SendRolesAsync(method, $"roles-by-id/{roleId}/composites", roles, ct);

    /// <summary>Atama ve kaldırma aynı gövdeyle; kaldırmada da gövde var (DELETE).</summary>
    private async Task SendRolesAsync(HttpMethod method, string path, IReadOnlyList<RoleRef> roles, CancellationToken ct)
    {
        if (roles.Count == 0)
        {
            return;
        }

        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(roles) };
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static RoleRef Ref(DirectoryRole role) => new(role.Id.ToString(), role.Name);

    private static DirectoryRoleKind KindOf(Dictionary<string, string[]>? attributes) =>
        attributes?.GetValueOrDefault(KindAttribute)?.FirstOrDefault() switch
        {
            PermissionKind => DirectoryRoleKind.Permission,
            StaffRoleKind => DirectoryRoleKind.StaffRole,
            _ => DirectoryRoleKind.Other
        };

    private static bool IsStaff(UserRepresentation user) =>
        !user.Username.StartsWith(ServiceAccountPrefix, StringComparison.Ordinal);

    private static DirectoryUser ToDirectoryUser(UserRepresentation user) => new(
        Guid.Parse(user.Id),
        user.Email ?? user.Username,
        user.FirstName,
        user.LastName,
        user.Enabled,
        DateTimeOffset.FromUnixTimeMilliseconds(user.CreatedTimestamp),
        user.RequiredActions ?? []);

    private sealed record RoleRef(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record RoleRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("attributes")] Dictionary<string, string[]>? Attributes);

    private sealed record UserRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("createdTimestamp")] long CreatedTimestamp,
        [property: JsonPropertyName("requiredActions")] string[]? RequiredActions);
}
