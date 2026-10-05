using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HiWallet.WalletConsumer.Identity;

/// <summary>
/// Servisin kendi token'ı, istemci kimlik bilgileriyle. Süresi dolmadan yeniden
/// kullanılıyor; dolmak üzereyken tek bir istek yenisini alıyor.
/// </summary>
internal sealed class ServiceTokens(HttpClient http, IOptions<KeycloakOptions> options, TimeProvider time)
{
    /// <summary>Token bu kadar süre kala yenileniyor: yolda süresi dolmasın.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refresh = new(1, 1);
    private (string Token, DateTimeOffset ExpiresAt)? _current;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (Fresh() is { } token) return token;

        await _refresh.WaitAsync(ct);

        try
        {
            if (Fresh() is { } refreshed) return refreshed;

            var settings = options.Value;
            using var response = await http.PostAsync(
                $"realms/{settings.Realm}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret
                }),
                ct);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                       ?? throw new InvalidOperationException("Kimlik sağlayıcı boş token döndü.");

            _current = (body.AccessToken, time.GetUtcNow().AddSeconds(body.ExpiresIn));
            return body.AccessToken;
        }
        finally
        {
            _refresh.Release();
        }
    }

    private string? Fresh() =>
        _current is { } current && time.GetUtcNow() < current.ExpiresAt - RefreshMargin ? current.Token : null;

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
