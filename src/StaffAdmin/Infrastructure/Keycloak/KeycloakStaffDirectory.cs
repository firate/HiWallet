using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HiWallet.StaffAdmin.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace HiWallet.StaffAdmin.Infrastructure.Keycloak;

/// <summary>
/// Keycloak'ın yönetim API'si, çalışanların realm'inin adresine göre
/// (<c>admin/realms/{realm}/</c>). Yalnızca kullanıcılar: açma, davet, kapatma. Roller ve
/// izinler Keycloak'ta tutulmuyor.
/// </summary>
internal sealed class KeycloakStaffDirectory(HttpClient http, IOptions<KeycloakOptions> options) : IStaffDirectory
{
    public async Task<DirectoryUser?> FindUserByEmailAsync(string email, CancellationToken ct)
    {
        var users = await http.GetFromJsonAsync<UserRepresentation[]>(
            $"users?briefRepresentation=true&exact=true&email={Uri.EscapeDataString(email)}", ct) ?? [];

        return users.Select(u => new DirectoryUser(Guid.Parse(u.Id), u.Enabled)).FirstOrDefault();
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

    private sealed record UserRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("enabled")] bool Enabled);
}
