using HiWallet.PersonalWebBff;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Bireysel web uygulamasının BFF'i. Oturum <see cref="TestSessions"/>'tan; kimlik
/// sağlayıcının adresleri discovery'den değil buradan geliyor, Keycloak testte ayakta değil.
/// </summary>
public sealed class PersonalWebBffFactory(
    HttpMessageHandler? walletApi = null,
    HttpMessageHandler? withdrawalOrchestrator = null)
    : EdgeApiFactory<PersonalWebBffApp>(TestTokens.InternalAudience, walletApi, withdrawalOrchestrator, rateLimitBurst: null)
{
    public const string ClientId = "personal-web";

    public static readonly string AuthorizationEndpoint = $"{TestTokens.Issuer}/protocol/openid-connect/auth";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ClientId"] = ClientId,
                ["Authentication:ClientSecret"] = "test-gizli-anahtar",
                ["Authentication:RequireHttpsMetadata"] = "false"
            }));

        builder.ConfigureTestServices(services =>
        {
            TestSessions.Use(services);

            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = TestTokens.Issuer,
                    AuthorizationEndpoint = AuthorizationEndpoint,
                    TokenEndpoint = $"{TestTokens.Issuer}/protocol/openid-connect/token",
                    EndSessionEndpoint = $"{TestTokens.Issuer}/protocol/openid-connect/logout"
                };

                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }
}
