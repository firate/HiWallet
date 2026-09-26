using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Testlerin kimlik sağlayıcısı. Token'ları kendi anahtarıyla imzalıyor ve host'ların
/// doğrulayıcısına o anahtarı veriyor; doğrulamanın geri kalanı (issuer, audience,
/// süre, imza) canlıdaki gibi koşuyor. Keycloak testte ayakta değil.
/// </summary>
public static class TestTokens
{
    public const string Issuer = "https://idp.hiwallet.test/realms/hiwallet";

    public const string Audience = "hiwallet-api";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "integration-tests" };

    public static IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Authentication:Issuer"] = Issuer,
        ["Authentication:Audience"] = Audience
    };

    /// <summary>Host'un doğrulayıcısı anahtarı kimlik sağlayıcıdan değil buradan alıyor.</summary>
    public static void Trust(IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(SigningKey);

            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
    }

    /// <summary>Seeder'ın açtığı hesabın kullanıcısı. Hesap kimliğini bilen test token'ı da üretebiliyor.</summary>
    public static string SubjectOf(Guid accountId) => $"test-{accountId:N}";

    public static string For(string subject, SecurityKey? signingKey = null, string audience = Audience)
    {
        var now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["sub"] = subject },
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddHours(1),
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256)
        });
    }

    public static HttpClient As(this HttpClient client, string subject)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", For(subject));
        return client;
    }

    public static HttpClient AsOwnerOf(this HttpClient client, Guid accountId) =>
        client.As(SubjectOf(accountId));
}
