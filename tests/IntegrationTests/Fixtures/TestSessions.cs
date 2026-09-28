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
/// servisler onu yeniden doğruluyor.
/// </summary>
public static class TestSessions
{
    public const string Scheme = "TestSession";
    public const string SubjectHeader = "X-Test-Subject";

    public static void Use(IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestSessionHandler>(Scheme, _ => { });

        services.PostConfigure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme, options => options.ForwardAuthenticate = Scheme);
    }

    public static HttpClient SignedInAs(this HttpClient client, string subject)
    {
        client.DefaultRequestHeaders.Remove(SubjectHeader);
        client.DefaultRequestHeaders.Add(SubjectHeader, subject);
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

    private sealed class TestSessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[SubjectHeader].ToString() is not { Length: > 0 } subject)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim("sub", subject), new Claim("name", "Deneme Kullanıcı"), new Claim("email", "deneme@hiwallet.test")],
                Scheme.Name,
                nameType: "name",
                roleType: null);

            var properties = new AuthenticationProperties();
            properties.StoreTokens(
            [
                new AuthenticationToken
                {
                    Name = "access_token",
                    Value = TestTokens.For(subject, audiences: [TestTokens.InternalAudience])
                }
            ]);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), properties, Scheme.Name)));
        }
    }
}
