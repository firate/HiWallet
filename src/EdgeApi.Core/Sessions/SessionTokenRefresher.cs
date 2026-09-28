using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiWallet.EdgeApi.Sessions;

/// <summary>
/// Oturumdaki access token'ı refresh token'la yeniler.
///
/// Aynı refresh token için tek istek: Keycloak refresh token'ı her kullanımda
/// değiştiriyor ve eskisini ikinci kez kabul etmiyor. Tarayıcı aynı anda birkaç API
/// isteği gönderiyor ve hepsi aynı cookie'yi, yani aynı refresh token'ı taşıyor; ayrı
/// ayrı yenileselerdi biri dışında hepsi reddedilir ve oturum kapanırdı. Sonuç bir
/// süre saklanıyor: yeni cookie tarayıcıya varmadan yola çıkmış istekler de onu alıyor.
///
/// Instance başına: BFF birden fazla instance'ta koşarsa aynı oturumun istekleri aynı
/// instance'a gitmeli.
/// </summary>
public sealed class SessionTokenRefresher(
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    TimeProvider time,
    ILogger<SessionTokenRefresher> logger)
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Attempt> _attempts = new();

    /// <returns>Yeni token'lar; kimlik sağlayıcı reddettiyse <c>null</c>, oturum bitmiştir.</returns>
    public async Task<RefreshedTokens?> RefreshAsync(string refreshToken)
    {
        var now = time.GetUtcNow();

        foreach (var (token, old) in _attempts)
        {
            if (now - old.StartedAt > Retention)
            {
                _attempts.TryRemove(new KeyValuePair<string, Attempt>(token, old));
            }
        }

        var attempt = _attempts.GetOrAdd(refreshToken, token =>
            new Attempt(now, new Lazy<Task<RefreshedTokens?>>(() => RequestAsync(token))));

        try
        {
            return await attempt.Result.Value;
        }
        catch
        {
            // Ağ hatası saklanmıyor: bir sonraki istek yeniden deniyor.
            _attempts.TryRemove(new KeyValuePair<string, Attempt>(refreshToken, attempt));
            throw;
        }
    }

    /// <summary>
    /// İsteği başlatanın iptaline bağlı DEĞİL: aynı sonucu bekleyen başka istekler var.
    /// </summary>
    private async Task<RefreshedTokens?> RequestAsync(string refreshToken)
    {
        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        using var timeout = new CancellationTokenSource(RequestTimeout);

        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(timeout.Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!
            })
        };

        using var response = await options.Backchannel.SendAsync(request, timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogInformation(
                "Kimlik sağlayıcı token yenilemeyi reddetti ({StatusCode}); oturum kapanıyor.", (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);

        return new RefreshedTokens(
            AccessToken: body.GetProperty("access_token").GetString()!,
            // Kimlik sağlayıcı yenisini vermediyse eskisi geçerli kalıyor.
            RefreshToken: body.TryGetProperty("refresh_token", out var rotated) ? rotated.GetString()! : refreshToken,
            ExpiresAt: time.GetUtcNow().AddSeconds(body.GetProperty("expires_in").GetInt32()));
    }

    private sealed record Attempt(DateTimeOffset StartedAt, Lazy<Task<RefreshedTokens?>> Result);
}

public sealed record RefreshedTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
