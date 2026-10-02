using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Tarayıcı arayüzünün BFF'i. Oturum <see cref="TestSessions"/>'tan; kimlik
/// sağlayıcının adresleri discovery'den değil buradan geliyor, Keycloak testte ayakta değil.
/// </summary>
/// <param name="issuer">BFF'in girişini yaptığı realm.</param>
/// <param name="staff">Oturumdaki token çalışanların realm'inden.</param>
public abstract class BffFactory<TEntryPoint>(
    string issuer,
    string clientId,
    bool staff,
    HttpMessageHandler? walletApi,
    HttpMessageHandler? withdrawalOrchestrator,
    HttpMessageHandler? onboarding = null,
    HttpMessageHandler? staffAdmin = null)
    : EdgeApiFactory<TEntryPoint>(
        TestTokens.InternalAudience, walletApi, withdrawalOrchestrator, rateLimitBurst: null, onboarding, staffAdmin)
    where TEntryPoint : class
{
    public string AuthorizationEndpoint => $"{issuer}/protocol/openid-connect/auth";

    public string ClientId => clientId;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Issuer"] = issuer,
                ["Authentication:ClientId"] = clientId,
                ["Authentication:ClientSecret"] = "test-gizli-anahtar",
                ["Authentication:RequireHttpsMetadata"] = "false"
            }));

        builder.ConfigureTestServices(services =>
        {
            TestSessions.Use(services, staff);

            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = issuer,
                    AuthorizationEndpoint = AuthorizationEndpoint,
                    TokenEndpoint = $"{issuer}/protocol/openid-connect/token",
                    EndSessionEndpoint = $"{issuer}/protocol/openid-connect/logout"
                };

                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }
}
