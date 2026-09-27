using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HiWallet.EdgeApi.Sessions;

/// <summary>
/// BFF'in tarayıcı oturumu. Tarayıcı yalnızca HttpOnly bir cookie taşıyor; token'lar
/// cookie'nin içinde, BFF'in anahtarıyla şifreli. Tarayıcıdaki kod onları okuyamıyor,
/// BFF dışında kimse çözemiyor.
///
/// Giriş Keycloak'la kod akışı (PKCE): token'ı BFF kendi istemci kimliğiyle alıyor.
/// İç servise giden istek token'ı oturumdan okuyor (<c>ForwardAccessTokenHandler</c>);
/// iç servis onu mobil uygulamanınki gibi yeniden doğruluyor.
/// </summary>
public static class BffSessionSetup
{
    /// <summary>
    /// <c>__Host-</c> öneki tarayıcıya cookie'yi yalnızca HTTPS'te, yalnızca bu adrese
    /// ve kök yola yazdırıyor; alt alan adından üstüne yazılamıyor.
    /// </summary>
    public const string CookieName = "__Host-hiwallet";

    /// <param name="applicationName">
    /// Şifreleme anahtarlarının kapsamı. Aynı dizini paylaşan başka bir BFF bu BFF'in
    /// cookie'sini çözemiyor.
    /// </param>
    public static IServiceCollection AddBffSession(this IServiceCollection services, string applicationName)
    {
        services.AddOptions<SessionSettings>()
            .BindConfiguration(SessionSettings.SectionName)
            .Validate(
                settings => Uri.TryCreate(settings.Issuer, UriKind.Absolute, out _),
                $"{SessionSettings.SectionName}:Issuer boş ya da mutlak bir adres değil.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.ClientId),
                $"{SessionSettings.SectionName}:ClientId boş.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.ClientSecret),
                $"{SessionSettings.SectionName}:ClientSecret boş.")
            // Fail fast: eksik ayar ilk girişte değil, başlangıçta patlasın.
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SessionTokenRefresher>();

        // Anahtarlar dizinde düz duruyor: dizinin erişimi korunuyor, anahtar kasası
        // kapsam dışı.
        services.AddDataProtection().SetApplicationName(applicationName);
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<SessionSettings>, ILoggerFactory>((options, settings, loggers) =>
            {
                if (settings.Value.KeysDirectory is { Length: > 0 } directory)
                {
                    options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(directory), loggers);
                }
            });

        // Kimlik okuması ve challenge cookie'de: API isteği Keycloak'a yönlendirilmiyor,
        // 401 alıyor. Girişi uygulama /bff/login ile başlatıyor.
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;

                // Keycloak'taki oturumun boşta kalma süresiyle aynı. Yenileme başarısız
                // olursa oturum bundan önce de kapanıyor.
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.SlidingExpiration = true;

                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = SessionTokens.RefreshIfExpiringAsync;
            })
            .AddOpenIdConnect();

        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<SessionSettings>>((oidc, options) =>
            {
                var settings = options.Value;

                oidc.Authority = settings.Issuer;
                oidc.RequireHttpsMetadata = settings.RequireHttpsMetadata;

                // Issuer tarayıcının gördüğü adres; discovery ve token ucu iç ağdan.
                if (!string.IsNullOrWhiteSpace(settings.MetadataAddress))
                {
                    oidc.MetadataAddress = settings.MetadataAddress;
                }

                oidc.ClientId = settings.ClientId;
                oidc.ClientSecret = settings.ClientSecret;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;

                // Token'lar cookie'nin içinde saklanıyor; iç servise giden istek ve
                // yenileme onları oradan okuyor.
                oidc.SaveTokens = true;

                // Claim adları token'daki gibi: "sub" .NET'in uzun URI'sine çevrilmiyor.
                oidc.MapInboundClaims = false;
                oidc.TokenValidationParameters.NameClaimType = "name";
                oidc.TokenValidationParameters.RoleClaimType = AuthenticationSetup.RolesClaim;
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    /// <summary>
    /// Kimliğin kullanıcıya gösterilen kısmı; token'lar tarayıcıya gitmiyor. Roller
    /// arayüzün hangi işi göstereceği için; yetkiyi iç servis kendisi kontrol ediyor.
    /// </summary>
    public static SessionUser ToSessionUser(this System.Security.Claims.ClaimsPrincipal user) =>
        new(
            user.Subject(),
            user.FindFirst("name")?.Value,
            user.FindFirst("email")?.Value,
            [.. user.FindAll(AuthenticationSetup.RolesClaim).Select(claim => claim.Value)]);
}

/// <param name="Roles">Çalışanın rolleri. Müşteride boş.</param>
public sealed record SessionUser(string Subject, string? Name, string? Email, IReadOnlyList<string> Roles);
