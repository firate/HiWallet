using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HiWallet.EdgeApi.Sessions;

/// <summary>
/// Tarayıcının oturumu: giriş, çıkış ve oturumdaki kullanıcı. Giriş ve çıkış sayfa
/// geçişiyle (Keycloak'a gidip dönülüyor), kullanıcı bilgisi API çağrısıyla.
///
/// Soyut: her BFF kendi controller'ını bundan türetiyor ve rotayı veriyor. Soyut sınıf
/// controller olarak keşfedilmiyor; bu kütüphaneyi kullanan token'lı ön API'lerde oturum
/// uçları açılmıyor.
/// </summary>
public abstract class SessionControllerBase : ControllerBase
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
    /// <param name="reauthenticate">
    /// Oturum açık olsa da parolayı yeniden sor (<c>prompt=login</c>). Telefon değiştirme gibi
    /// işlemler yakın zamanda yapılmış bir giriş istiyor; yeni token'ın <c>auth_time</c>'ı şimdi.
    /// </param>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(
        [FromQuery] string? returnUrl, [FromQuery] string? loginHint, [FromQuery] bool reauthenticate = false)
    {
        var properties = new AuthenticationProperties { RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" };

        if (loginHint is { Length: > 0 and <= 254 })
        {
            properties.Items[BffSessionSetup.LoginHintItem] = loginHint;
        }

        if (reauthenticate)
        {
            properties.SetParameter(OpenIdConnectParameterNames.Prompt, "login");
        }

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Cookie'yi siler ve Keycloak'taki oturumu da kapatır; aynı tarayıcıda yeniden
    /// girişte parola soruluyor. Form ile gönderiliyor, sayfa Keycloak'a gidip dönüyor.
    ///
    /// Oturumu olan herkese açık: hiçbir izni olmayan çalışan da başka bir kullanıcıyla
    /// girmek için çıkabilmeli.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
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
