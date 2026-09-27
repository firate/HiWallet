using System.Net;
using System.Security.Claims;
using System.Text;
using HiWallet.EdgeApi.Sessions;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// BFF oturumundaki access token'ın yenilenmesi. Keycloak refresh token'ı her
/// kullanımda değiştiriyor ve eskisini ikinci kez kabul etmiyor; aynı anda gelen iki
/// istek ayrı ayrı yenileseydi ikincisi reddedilir ve kullanıcının oturumu kapanırdı.
/// </summary>
public sealed class SessionTokenRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private sealed class TokenEndpoint(HttpStatusCode status, TaskCompletionSource? gate = null) : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);

            if (gate is not null)
            {
                await gate.Task;
            }

            var body = status == HttpStatusCode.OK
                ? """{"access_token":"yeni-access","refresh_token":"yeni-refresh","expires_in":300,"token_type":"Bearer"}"""
                : """{"error":"invalid_grant"}""";

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ServiceProvider Services(TokenEndpoint endpoint)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddAuthentication().AddCookie();
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme).Configure(options =>
        {
            options.ClientId = "personal-web";
            options.ClientSecret = "test-gizli-anahtar";
            options.Backchannel = new HttpClient(endpoint);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { TokenEndpoint = "https://idp.hiwallet.test/token" });
        });
        services.AddSingleton<SessionTokenRefresher>();

        return services.BuildServiceProvider();
    }

    private static CookieValidatePrincipalContext Session(IServiceProvider services, DateTimeOffset expiresAt)
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = "eski-access" },
            new AuthenticationToken { Name = "refresh_token", Value = "eski-refresh" },
            new AuthenticationToken { Name = "expires_at", Value = expiresAt.ToString("o") }
        ]);

        var context = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-oturum")], "test")), properties, scheme.Name);

        return new CookieValidatePrincipalContext(context, scheme, new CookieAuthenticationOptions(), ticket);
    }

    [Fact]
    public async Task EsZamanliIstekler_TekYenilemePaylasiyor()
    {
        var gate = new TaskCompletionSource();
        var endpoint = new TokenEndpoint(HttpStatusCode.OK, gate);
        await using var services = Services(endpoint);
        var refresher = services.GetRequiredService<SessionTokenRefresher>();

        var first = refresher.RefreshAsync("eski-refresh");
        var second = refresher.RefreshAsync("eski-refresh");
        gate.SetResult();

        var results = await Task.WhenAll(first, second);

        endpoint.Calls.ShouldBe(1);
        results[0].ShouldNotBeNull().AccessToken.ShouldBe("yeni-access");
        results[1].ShouldBe(results[0]);
    }

    [Fact]
    public async Task SuresiDolmakUzere_YenileniyorVeCookieYenidenYaziliyor()
    {
        await using var services = Services(new TokenEndpoint(HttpStatusCode.OK));
        var session = Session(services, expiresAt: Now.AddSeconds(10));

        await SessionTokens.RefreshIfExpiringAsync(session);

        session.Principal.ShouldNotBeNull();
        session.ShouldRenew.ShouldBeTrue();
        session.Properties.GetTokenValue("access_token").ShouldBe("yeni-access");
        session.Properties.GetTokenValue("refresh_token").ShouldBe("yeni-refresh");
        DateTimeOffset.Parse(session.Properties.GetTokenValue("expires_at")!).ShouldBe(Now.AddSeconds(300));
    }

    [Fact]
    public async Task SuresiUzun_KeycloakaGidilmiyor()
    {
        var endpoint = new TokenEndpoint(HttpStatusCode.OK);
        await using var services = Services(endpoint);
        var session = Session(services, expiresAt: Now.AddMinutes(4));

        await SessionTokens.RefreshIfExpiringAsync(session);

        endpoint.Calls.ShouldBe(0);
        session.ShouldRenew.ShouldBeFalse();
        session.Properties.GetTokenValue("access_token").ShouldBe("eski-access");
    }

    /// <summary>
    /// Keycloak'taki oturum bitmiş (süre, çıkış, iptal): BFF'in oturumu da kapanıyor ve
    /// cookie siliniyor. Bir sonraki API çağrısı 401 alıyor, uygulama girişe dönüyor.
    /// </summary>
    [Fact]
    public async Task KeycloakReddederse_OturumKapaniyor()
    {
        await using var services = Services(new TokenEndpoint(HttpStatusCode.BadRequest));
        var session = Session(services, expiresAt: Now.AddSeconds(-5));

        await SessionTokens.RefreshIfExpiringAsync(session);

        session.Principal.ShouldBeNull();
        session.HttpContext.Response.Headers.SetCookie.ToString().ShouldContain("expires=Thu, 01 Jan 1970");
    }
}
