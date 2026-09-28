using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.EdgeApi.Sessions;

/// <summary>
/// Cookie her okunduğunda: access token'ın süresi dolmak üzereyse yenileniyor ve cookie
/// yeni token'larla yeniden yazılıyor. İç servise giden istek her zaman geçerli token
/// taşıyor; tarayıcı yenilemeden habersiz.
/// </summary>
public static class SessionTokens
{
    /// <summary>İç servise giderken yolda dolmasın diye süre bitmeden bu kadar önce yenileniyor.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

    public static async Task RefreshIfExpiringAsync(CookieValidatePrincipalContext context)
    {
        if (context.Properties.GetTokenValue("expires_at") is not { } expiresAt
            || context.Properties.GetTokenValue("refresh_token") is not { } refreshToken)
        {
            return;
        }

        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        if (DateTimeOffset.Parse(expiresAt, CultureInfo.InvariantCulture) - now > RefreshMargin)
        {
            return;
        }

        var refreshed = await services.GetRequiredService<SessionTokenRefresher>().RefreshAsync(refreshToken);

        // Kimlik sağlayıcıdaki oturum bitmiş: cookie de siliniyor, yoksa her istek aynı
        // ölü refresh token'la yeniden denerdi.
        if (refreshed is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
            return;
        }

        context.Properties.UpdateTokenValue("access_token", refreshed.AccessToken);
        context.Properties.UpdateTokenValue("refresh_token", refreshed.RefreshToken);
        context.Properties.UpdateTokenValue("expires_at", refreshed.ExpiresAt.ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }
}
