using HiWallet.EdgeApi.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.PersonalWebBff.Controllers;

/// <summary>
/// Tarayıcının oturumu: giriş, çıkış ve oturumdaki kullanıcı. Giriş ve çıkış sayfa
/// geçişiyle (Keycloak'a gidip dönülüyor), kullanıcı bilgisi API çağrısıyla.
/// </summary>
[ApiController]
[Route("bff")]
public sealed class SessionController : ControllerBase
{
    /// <summary>Keycloak'ın giriş sayfasına yönlendirir; dönüşte cookie yazılıyor.</summary>
    /// <param name="returnUrl">
    /// Girişten sonra açılacak sayfa. Yalnızca bu sitenin yolu; başka adres verilirse
    /// ana sayfa. Aksi halde giriş bağlantısı başka siteye yönlendirmek için kullanılırdı.
    /// </param>
    /// <param name="loginHint">
    /// Formda dolu gelecek e-posta: kaydı yeni biten müşteri adresini ikinci kez yazmıyor.
    /// Yalnızca ipucu; kimlik sağlayıcı yine parolayı soruyor.
    /// </param>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl, [FromQuery] string? loginHint)
    {
        var properties = new AuthenticationProperties { RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" };

        if (loginHint is { Length: > 0 and <= 254 })
        {
            properties.Items[BffSessionSetup.LoginHintItem] = loginHint;
        }

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Cookie'yi siler ve Keycloak'taki oturumu da kapatır; aynı tarayıcıda yeniden
    /// girişte parola soruluyor. Form ile gönderiliyor, sayfa Keycloak'a gidip dönüyor.
    /// </summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>Oturumdaki kullanıcı. Oturum yoksa 401; uygulama girişi buradan anlıyor.</summary>
    [HttpGet("user")]
    [ProducesResponseType<SessionUser>(StatusCodes.Status200OK)]
    public SessionUser Get() => User.ToSessionUser();
}
