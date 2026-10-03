using System.Net.Http.Headers;
using System.Security.Cryptography;
using HiWallet.Shared.Infrastructure.Authentication;
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

    /// <summary>Çalışanların realm'i: müşterininkinden ayrı bir kimlik sağlayıcı adresi.</summary>
    public const string StaffIssuer = "https://idp.hiwallet.test/realms/hiwallet-staff";

    /// <summary>İç servislerin (wallet-api, orchestrator) kabul ettiği hedef kitle.</summary>
    public const string InternalAudience = "hiwallet-api";

    /// <summary>
    /// Her ön API yalnızca kendisi için verilmiş token'ı kabul ediyor: mobil uygulamanın
    /// token'ı business-api'de, işyerinin token'ı mobil ön API'de geçmiyor.
    /// </summary>
    public const string PersonalMobileAudience = "personal-mobile-api";

    public const string BusinessAudience = "business-api";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "integration-tests" };

    public static IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Authentication:Issuer"] = Issuer,
        ["Authentication:Audience"] = InternalAudience,
        // Yalnızca çalışan token'ını kabul eden servis okuyor.
        ["Authentication:Staff:Issuer"] = StaffIssuer,
        // İzin kaynağı testte TestStaffPermissions ya da gerçek personel yönetimi; adres
        // yalnızca başlangıçtaki doğrulamayı geçmek için.
        ["Authentication:Staff:StaffAdminUrl"] = "http://staff-admin.test"
    };

    /// <summary>
    /// Host'un doğrulayıcısı anahtarı kimlik sağlayıcıdan değil buradan alıyor. İki realm
    /// de aynı anahtarla imzalıyor; ayrımı issuer yapıyor.
    /// </summary>
    public static void Trust(IServiceCollection services)
    {
        Trust(services, JwtBearerDefaults.AuthenticationScheme, Issuer);
        Trust(services, AuthenticationSetup.StaffScheme, StaffIssuer);
    }

    private static void Trust(IServiceCollection services, string scheme, string issuer)
    {
        services.PostConfigure<JwtBearerOptions>(scheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = issuer };
            configuration.SigningKeys.Add(SigningKey);

            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
    }

    /// <summary>Seeder'ın açtığı hesabın kullanıcısı. Hesap kimliğini bilen test token'ı da üretebiliyor.</summary>
    public static string SubjectOf(Guid accountId) => $"test-{accountId:N}";

    /// <param name="audiences">
    /// Verilmezse mobil uygulamanın token'ı: mobil ön API ve iç servisler için.
    /// </param>
    /// <param name="authorizedParty">
    /// Token'ı alan istemci (<c>azp</c>). Servislerin kendi token'larında istemcinin adı.
    /// </param>
    public static string For(
        string subject,
        SecurityKey? signingKey = null,
        string[]? audiences = null,
        string issuer = Issuer,
        string? authorizedParty = null)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["aud"] = audiences ?? [PersonalMobileAudience, InternalAudience]
        };

        if (authorizedParty is not null)
        {
            claims["azp"] = authorizedParty;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Claims = claims,
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

    /// <summary>İşyerinin sistem entegrasyonu: business-api ve iç servisler için token.</summary>
    public static HttpClient AsIntegration(this HttpClient client, string subject)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", For(subject, audiences: [BusinessAudience, InternalAudience]));
        return client;
    }

    public static HttpClient AsIntegrationOf(this HttpClient client, Guid accountId) =>
        client.AsIntegration(SubjectOf(accountId));

    /// <summary>
    /// Çalışanın token'ı: çalışanların realm'inden, iç servisler için. Token izin taşımıyor;
    /// izinler <see cref="TestStaffPermissions"/>'a yazılıyor.
    /// </summary>
    public static string ForStaff(string subject, params string[] permissions)
    {
        TestStaffPermissions.Grant(subject, permissions);
        return For(subject, audiences: [InternalAudience], issuer: StaffIssuer);
    }

    public static HttpClient AsStaff(this HttpClient client, string subject, params string[] permissions)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ForStaff(subject, permissions));
        return client;
    }

    public const string OnboardingClientId = "onboarding";

    /// <summary>
    /// Onboarding servisinin kendi token'ı (client credentials): kimlik istemcinin servis
    /// hesabı, <c>azp</c> istemcinin adı.
    /// </summary>
    public static HttpClient AsOnboarding(this HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", For(
                $"service-account-{OnboardingClientId}",
                audiences: [InternalAudience],
                authorizedParty: OnboardingClientId));
        return client;
    }
}
