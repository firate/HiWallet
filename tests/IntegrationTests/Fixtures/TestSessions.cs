using System.Security.Claims;
using System.Text.Encodings.Web;
using HiWallet.EdgeApi.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// BFF'in tarayıcı oturumu testte. Keycloak'la giriş yapılmadan oturum açık sayılıyor:
/// cookie şeması kimliği bu şemadan okuyor, geri kalanı (401'e dönen challenge, CSRF
/// başlığı, token'ın iç servise iletilmesi) canlıdaki gibi koşuyor.
///
/// Oturumdaki access token <see cref="TestTokens"/>'ın imzaladığı gerçek bir token; iç
/// servisler onu yeniden doğruluyor. Çalışanın oturumunda token çalışanların realm'inden;
/// izinleri <see cref="TestStaffPermissions"/>'ta.
/// </summary>
public static class TestSessions
{
    public const string Scheme = "TestSession";
    public const string SubjectHeader = "X-Test-Subject";
    public const string PermissionsHeader = "X-Test-Permissions";

    /// <param name="staff">Oturum çalışanın: token çalışanların realm'inden.</param>
    public static void Use(IServiceCollection services, bool staff = false)
    {
        services.AddAuthentication()
            .AddScheme<TestSessionOptions, TestSessionHandler>(Scheme, options => options.Staff = staff);

        services.PostConfigure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme, options => options.ForwardAuthenticate = Scheme);
    }

    /// <param name="permissions">Çalışanın izinleri; müşteride boş.</param>
    public static HttpClient SignedInAs(this HttpClient client, string subject, params string[] permissions)
    {
        client.DefaultRequestHeaders.Remove(SubjectHeader);
        client.DefaultRequestHeaders.Remove(PermissionsHeader);
        client.DefaultRequestHeaders.Add(SubjectHeader, subject);

        if (permissions.Length > 0)
        {
            client.DefaultRequestHeaders.Add(PermissionsHeader, string.Join(',', permissions));
        }

        return client;
    }

    public static HttpClient SignedInAsOwnerOf(this HttpClient client, Guid accountId) =>
        client.SignedInAs(TestTokens.SubjectOf(accountId));

    /// <summary>Tarayıcıdaki uygulamanın her API isteğine eklediği başlık.</summary>
    public static HttpClient WithCsrfHeader(this HttpClient client)
    {
        client.DefaultRequestHeaders.Add(CsrfHeader.Name, CsrfHeader.Value);
        return client;
    }

    private sealed class TestSessionOptions : AuthenticationSchemeOptions
    {
        public bool Staff { get; set; }
    }

    private sealed class TestSessionHandler(
        IOptionsMonitor<TestSessionOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<TestSessionOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[SubjectHeader].ToString() is not { Length: > 0 } subject)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var permissions = Request.Headers[PermissionsHeader].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries);

            var identity = new ClaimsIdentity(
                [
                    new Claim("sub", subject),
                    new Claim("name", "Deneme Kullanıcı"),
                    new Claim("email", "deneme@hiwallet.test")
                ],
                Scheme.Name,
                nameType: "name",
                roleType: null);

            var properties = new AuthenticationProperties();
            properties.StoreTokens(
            [
                new AuthenticationToken
                {
                    Name = "access_token",
                    Value = Options.Staff
                        ? TestTokens.ForStaff(subject, permissions)
                        : TestTokens.For(subject, audiences: [TestTokens.InternalAudience])
                }
            ]);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), properties, Scheme.Name)));
        }
    }
}
