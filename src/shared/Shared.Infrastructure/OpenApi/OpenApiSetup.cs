using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace HiWallet.Shared.Infrastructure.OpenApi;

/// <summary>
/// OpenAPI dokümanı + Scalar arayüzü (baseline.md madde 9).
///
/// Doküman <c>Microsoft.AspNetCore.OpenApi</c> ile üretiliyor — framework'ün kendi
/// üreteci, Swashbuckle YOK. Üreteç yalnızca JSON veriyor; okunabilir arayüz için
/// Scalar ayrıca ekleniyor.
///
/// Servise özel hiçbir şey içermiyor, bu yüzden Shared'da: <c>wallet-api</c> ve
/// <c>withdrawal-orchestrator</c> aynı kurulumu istiyor ve iki yerde tutmanın anlamı
/// yok. <c>ObservabilitySetup</c> ile aynı gerekçe.
/// </summary>
public static class OpenApiSetup
{
    public const string SecuritySchemeName = "keycloak";

    /// <summary>
    /// Scalar'daki girişin istemcisi. Compose'daki realm'de tanımlı, canlıda YOK;
    /// Scalar da yalnızca Development'ta.
    /// </summary>
    public const string DocsClientId = "api-docs";

    /// <param name="flows">
    /// API'yi çağıranın token'ı hangi yoldan aldığı. Doküman bunu kimlik şeması olarak
    /// taşıyor ve Scalar giriş düğmesini buradan çıkarıyor. Kimlik istemeyen servis
    /// vermiyor.
    /// </param>
    public static IServiceCollection AddHiWalletOpenApi(
        this IServiceCollection services, TokenFlows flows = TokenFlows.None)
    {
        services.AddSingleton(new DocumentedTokenFlows(flows));

        return services.AddOpenApi(options =>
        {
            if (flows == TokenFlows.None)
            {
                return;
            }

            options.AddDocumentTransformer((document, context, _) =>
            {
                var settings = context.ApplicationServices.GetRequiredService<IOptions<TokenValidationSettings>>().Value;
                AddSecurityScheme(document, settings.Issuer!, flows);
                return Task.CompletedTask;
            });
        });
    }

    /// <summary>
    /// Adresler Keycloak'ın; issuer tarayıcının kimlik sağlayıcıya ulaştığı adres
    /// olduğu için giriş sayfası da oradan açılıyor.
    /// </summary>
    private static void AddSecurityScheme(OpenApiDocument document, string issuer, TokenFlows flows)
    {
        var tokenUrl = new Uri($"{issuer.TrimEnd('/')}/protocol/openid-connect/token");
        var oauthFlows = new OpenApiOAuthFlows();

        if (flows.HasFlag(TokenFlows.AuthorizationCode))
        {
            oauthFlows.AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"{issuer.TrimEnd('/')}/protocol/openid-connect/auth"),
                TokenUrl = tokenUrl,
                Scopes = new Dictionary<string, string>()
            };
        }

        if (flows.HasFlag(TokenFlows.ClientCredentials))
        {
            oauthFlows.ClientCredentials = new OpenApiOAuthFlow
            {
                TokenUrl = tokenUrl,
                Scopes = new Dictionary<string, string>()
            };
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SecuritySchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = oauthFlows
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = []
        });
    }

    /// <summary>
    /// Development kapısı BURADA, çağıran tarafta değil. Canlıda API yüzeyinin
    /// şemasını yayınlamak saldırgana harita vermek demek; kapıyı her serviste tekrar
    /// yazmak, bir gün birinde unutulması demekti. Unutulduğunda da hiçbir test
    /// kırılmaz, yalnızca endpoint sessizce açık kalır.
    /// </summary>
    public static WebApplication MapHiWalletOpenApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        var flows = app.Services.GetRequiredService<DocumentedTokenFlows>().Flows;

        // İki uç da kimliksiz açık: yalnızca Development'ta varlar ve şema bir sır değil,
        // uçları çağırmak yine token istiyor.

        // Doküman: /openapi/v1.json
        app.MapOpenApi().AllowAnonymous();

        // Arayüz: /scalar — dokümanı yukarıdaki endpoint'ten okuyor.
        app.MapScalarApiReference(options =>
            {
                options.WithTitle(app.Environment.ApplicationName);

                if (flows == TokenFlows.None)
                {
                    return;
                }

                options.AddPreferredSecuritySchemes([SecuritySchemeName]);

                // İşyerinin istemci kimliği ve gizli anahtarı sayfada elle giriliyor;
                // yalnızca müşteri girişinin istemcisi önceden dolu.
                if (flows.HasFlag(TokenFlows.AuthorizationCode))
                {
                    options.AddAuthorizationCodeFlow(SecuritySchemeName, flow => flow
                        .WithClientId(DocsClientId)
                        .WithPkce(Pkce.Sha256));
                }
            })
            .AllowAnonymous();

        return app;
    }

    private sealed record DocumentedTokenFlows(TokenFlows Flows);
}

/// <summary>API'yi çağıranın token'ı kimlik sağlayıcıdan hangi yoldan aldığı.</summary>
[Flags]
public enum TokenFlows
{
    None = 0,

    /// <summary>Kullanıcı tarayıcıda kimlik sağlayıcıya girip token alıyor (PKCE).</summary>
    AuthorizationCode = 1,

    /// <summary>Sistem entegrasyonu kendi istemci kimliği ve gizli anahtarıyla token alıyor.</summary>
    ClientCredentials = 2
}
