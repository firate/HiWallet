using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HiWallet.Shared.Infrastructure.Authentication;

/// <summary>
/// Kimlik sağlayıcının imzaladığı token'ı doğrular. Ön API'ler de iç servisler de bu
/// kurulumu kullanıyor: ön API token'ı iç servise AYNEN iletiyor ve iç servis onu
/// yeniden doğruluyor. Ön API'ye körü körüne güvenilmiyor; ele geçirilmiş bir ön API
/// başkası adına istek yazdıramıyor.
///
/// Varsayılan politika kimlik istiyor ve çalışanı dışarıda bırakıyor: yeni bir uç
/// kendiliğinden müşteriye açık, çalışana kapalı açılıyor. Açık kalması gereken uç
/// (sağlık, API dokümanı) ve çalışanın geçebildiği uç bunu kendisi söylüyor.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>Çalışanların realm'inin token'ını doğrulayan şema. Yalnızca iç servislerde.</summary>
    public const string StaffScheme = "StaffBearer";

    /// <summary>Token'ın hangi realm'den geldiğine bakıp doğrulamayı ilgili şemaya veren şema.</summary>
    private const string SelectorScheme = "Token";

    /// <summary>
    /// Doğrulanan kimliğin türü. Token'ın içeriğinden değil, onu doğrulayan şemadan
    /// geliyor: müşterinin realm'inden gelen bir token kendini çalışan diye
    /// tanıtamıyor.
    /// </summary>
    private const string StaffIdentityType = "hiwallet-staff";

    public const string SubjectClaim = "sub";

    /// <summary>Çalışanın realm rolleri; Keycloak token'a düz bir dizi olarak yazıyor.</summary>
    public const string RolesClaim = "roles";

    /// <param name="acceptStaffTokens">
    /// Çalışanların realm'inin token'ı da kabul ediliyor. Yalnızca iç servisler: çalışan
    /// kendi ön API'sinden (backoffice) geliyor, müşterinin ön API'leri onu tanımıyor.
    /// </param>
    public static IServiceCollection AddHiWalletAuthentication(
        this IServiceCollection services, bool acceptStaffTokens = false)
    {
        var settings = services.AddOptions<TokenValidationSettings>()
            .BindConfiguration(TokenValidationSettings.SectionName)
            .Validate(
                settings => Uri.TryCreate(settings.Issuer, UriKind.Absolute, out _),
                $"{TokenValidationSettings.SectionName}:Issuer boş ya da mutlak bir adres değil. Token'ı kimin imzaladığı bilinmeden doğrulanamaz.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.Audience),
                $"{TokenValidationSettings.SectionName}:Audience boş.");

        if (acceptStaffTokens)
        {
            settings.Validate(
                settings => Uri.TryCreate(settings.Staff.Issuer, UriKind.Absolute, out _),
                $"{TokenValidationSettings.SectionName}:Staff:Issuer boş ya da mutlak bir adres değil.");
        }

        // Fail fast: eksik ayar ilk istekte değil, başlangıçta patlasın.
        settings.ValidateOnStart();

        var authentication = services
            .AddAuthentication(acceptStaffTokens ? SelectorScheme : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        ConfigureBearer(services, JwtBearerDefaults.AuthenticationScheme, settings => (settings.Issuer, settings.MetadataAddress));

        if (acceptStaffTokens)
        {
            authentication.AddJwtBearer(StaffScheme);

            ConfigureBearer(services, StaffScheme, settings => (settings.Staff.Issuer, settings.Staff.MetadataAddress));
            services.AddOptions<JwtBearerOptions>(StaffScheme).Configure(jwt =>
            {
                jwt.TokenValidationParameters.AuthenticationType = StaffIdentityType;
                jwt.TokenValidationParameters.RoleClaimType = RolesClaim;
            });

            // Token imzası doğrulanmadan önce yalnızca yönlendirme için okunuyor; kararı
            // seçilen şemanın doğrulaması veriyor. Çalışanın adresini taşıyan sahte bir
            // token çalışan şemasının anahtarlarına takılıyor.
            authentication.AddPolicyScheme(SelectorScheme, displayName: null, options =>
                options.ForwardDefaultSelector = context =>
                {
                    var staffIssuer = context.RequestServices
                        .GetRequiredService<IOptions<TokenValidationSettings>>().Value.Staff.Issuer;

                    return IssuerOf(context) == staffIssuer ? StaffScheme : JwtBearerDefaults.AuthenticationScheme;
                });
        }

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.IsEmployee())
                .Build())
            .AddPolicy(HiWalletPolicies.CustomerOrStaff, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    !context.User.IsEmployee() || StaffRoles.All.Any(context.User.IsInRole)))
            .AddPolicy(HiWalletPolicies.Staff, policy => StaffWith(policy, StaffRoles.All))
            .AddPolicy(HiWalletPolicies.Marketing, policy => StaffWith(policy, [StaffRoles.Marketing]))
            .AddPolicy(HiWalletPolicies.Operations, policy => StaffWith(policy, [StaffRoles.Operations]))
            .AddPolicy(HiWalletPolicies.Finance, policy => StaffWith(policy, [StaffRoles.Finance]));

        return services;
    }

    /// <summary>Çalışanların realm'inden ve rollerden biriyle; müşterinin token'ı geçmiyor.</summary>
    private static void StaffWith(AuthorizationPolicyBuilder policy, IReadOnlyList<string> roles) =>
        policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.IsEmployee() && roles.Any(context.User.IsInRole));

    private static void ConfigureBearer(
        IServiceCollection services,
        string scheme,
        Func<TokenValidationSettings, (string? Issuer, string? MetadataAddress)> realm)
    {
        services.AddOptions<JwtBearerOptions>(scheme)
            .Configure<IOptions<TokenValidationSettings>>((jwt, options) =>
            {
                var settings = options.Value;
                var (issuer, metadataAddress) = realm(settings);

                jwt.Authority = issuer;
                jwt.Audience = settings.Audience;
                jwt.RequireHttpsMetadata = settings.RequireHttpsMetadata;

                // Issuer token'daki adres, yani istemcinin kimlik sağlayıcıya ulaştığı
                // public adres. İç ağdaki servis anahtarları başka bir adresten okuyabiliyor.
                if (!string.IsNullOrWhiteSpace(metadataAddress))
                {
                    jwt.MetadataAddress = metadataAddress;
                }

                // Claim adları token'daki gibi kalıyor: "sub" .NET'in uzun URI'sine
                // çevrilmiyor.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.NameClaimType = SubjectClaim;

                // Ön API token'ı iç servise iletiyor; iletirken buradan okuyor.
                jwt.SaveToken = true;
            });
    }

    private static string? IssuerOf(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return new JsonWebToken(header["Bearer ".Length..].Trim()).Issuer;
        }
        catch (ArgumentException)
        {
            // Okunamayan token müşteri şemasına gidiyor ve orada reddediliyor.
            return null;
        }
    }

    /// <summary>Kimlik sağlayıcıdaki kullanıcı kimliği. Doğrulanmış token'da her zaman var.</summary>
    public static string Subject(this ClaimsPrincipal user) =>
        user.FindFirst(SubjectClaim)?.Value
        ?? throw new InvalidOperationException("Token'da 'sub' yok.");

    /// <summary>Token çalışanların realm'inden geldi ve orada doğrulandı.</summary>
    public static bool IsEmployee(this ClaimsPrincipal user) =>
        user.Identities.Any(identity => identity is { IsAuthenticated: true, AuthenticationType: StaffIdentityType });
}

/// <summary>Çalışanların realm rolleri: iş grubuna göre.</summary>
public static class StaffRoles
{
    /// <summary>Müşteri kaydını görüntüler.</summary>
    public const string Support = "support";

    /// <summary>Ters kayıt ve çekim iptali.</summary>
    public const string Operations = "operations";

    /// <summary>Koruma hesabının fonlanması.</summary>
    public const string Finance = "finance";

    /// <summary>Kampanya, personel promo'su, işyerinin promo kabulü.</summary>
    public const string Marketing = "marketing";

    /// <summary>Her rol müşteri kaydını görüntüleyebiliyor; yazma işi rolün kendi ucunda.</summary>
    public static readonly IReadOnlyList<string> All = [Support, Operations, Finance, Marketing];
}

public static class HiWalletPolicies
{
    /// <summary>
    /// Müşteri kendi kaynağını, çalışan bir rolüyle her müşterinin kaynağını görüntülüyor.
    /// Yalnızca okuma uçlarında; müşterinin para hareketi başlatan uçları çalışana kapalı.
    /// </summary>
    public const string CustomerOrStaff = "customer-or-staff";

    /// <summary>Herhangi bir rolü olan çalışan. Müşteriye kapalı görüntüleme uçları.</summary>
    public const string Staff = "staff";

    /// <summary>Kampanya, personel promo'su, işyerinin promo kabulü.</summary>
    public const string Marketing = "staff-marketing";

    /// <summary>Ters kayıt ve çekim incelemesi.</summary>
    public const string Operations = "staff-operations";

    /// <summary>Koruma hesabının fonlanması.</summary>
    public const string Finance = "staff-finance";
}

public sealed class TokenValidationSettings
{
    public const string SectionName = "Authentication";

    /// <summary>Token'daki <c>iss</c>: kimlik sağlayıcının istemcinin gördüğü adresi.</summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// İmza anahtarlarının okunduğu discovery adresi. Verilmezse issuer'dan türetiliyor;
    /// iç ağdan farklı bir adresle ulaşılıyorsa burada veriliyor.
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Token'ın bu API'ler için verildiğini gösteren <c>aud</c>.</summary>
    public string? Audience { get; set; }

    /// <summary>Canlıda kapatılmaz. Compose'da kimlik sağlayıcı düz HTTP ile konuşuyor.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Çalışanların realm'i. Hedef kitle ve HTTPS ayarı müşterininkiyle aynı.</summary>
    public RealmSettings Staff { get; set; } = new();

    public sealed class RealmSettings
    {
        public string? Issuer { get; set; }

        public string? MetadataAddress { get; set; }
    }
}
