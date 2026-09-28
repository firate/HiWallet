using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.Onboarding.Application.Abstractions;

namespace HiWallet.Onboarding.Infrastructure.Keycloak;

/// <summary>
/// Keycloak'ın yönetim API'si, realm'in adresine göre (<c>admin/realms/{realm}/</c>).
/// Kullanıcı adı e-posta; e-posta bu servis tarafından doğrulandı, Keycloak'a doğrulanmış
/// olarak yazılıyor.
/// </summary>
internal sealed class KeycloakIdentityProvider(HttpClient http) : IIdentityProvider
{
    public async Task<IdentityUser?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var users = await http.GetFromJsonAsync<JsonElement[]>(
            $"users?email={Uri.EscapeDataString(email)}&exact=true", ct) ?? [];

        return users
            .Select(u => new IdentityUser(u.GetProperty("id").GetString()!, u.GetProperty("email").GetString()!))
            .FirstOrDefault();
    }

    public async Task<string> CreateUserAsync(string email, string password, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("users", new
        {
            username = email,
            email,
            emailVerified = true,
            enabled = true,
            credentials = new[] { new { type = "password", value = password, temporary = false } }
        }, ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new IdentityConflictException(email);
        }

        await ThrowIfPasswordRejectedAsync(response, ct);
        response.EnsureSuccessStatusCode();

        // Kullanıcının kimliği gövdede değil, Location'ın son parçasında.
        return response.Headers.Location?.Segments.LastOrDefault()?.TrimEnd('/')
               ?? throw new InvalidOperationException("Kimlik sağlayıcı açılan kullanıcının adresini dönmedi.");
    }

    public async Task SetPasswordAsync(string subject, string password, CancellationToken ct)
    {
        using var response = await http.PutAsJsonAsync(
            $"users/{Uri.EscapeDataString(subject)}/reset-password",
            new { type = "password", value = password, temporary = false }, ct);

        await ThrowIfPasswordRejectedAsync(response, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Parola kuralına uymayan parolayı Keycloak <c>400</c> ile reddediyor.</summary>
    private static async Task ThrowIfPasswordRejectedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.BadRequest) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new PasswordRejectedException($"Parola kimlik sağlayıcının kuralına uymuyor: {body}");
    }
}
