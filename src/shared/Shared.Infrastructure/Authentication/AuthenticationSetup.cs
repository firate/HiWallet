using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
///
/// Çalışanın yetkisi İZİNLE kontrol ediliyor (<see cref="StaffPermissions"/>), rolle
/// değil. Token'da izin yok: izinler personel yönetiminin veritabanında ve her istekte
/// oradan okunuyor (<see cref="IStaffPermissions"/>). Rolü alınan çalışanın bir sonraki
/// isteği reddediliyor.
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

    /// <summary>Çalışanın izinlerini soran istemcinin tek denemesinin sınırı.</summary>
    private static readonly TimeSpan StaffPermissionsTimeout = TimeSpan.FromSeconds(5);

    /// <param name="acceptStaffTokens">
    /// Çalışanların realm'inin token'ı da kabul ediliyor. Yalnızca iç servisler: çalışan
    /// kendi ön API'sinden (backoffice) geliyor, müşterinin ön API'leri onu tanımıyor.
    /// Çalışanın izinleri her istekte personel yönetimine soruluyor.
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
            settings
                .Validate(
                    settings => Uri.TryCreate(settings.Staff.Issuer, UriKind.Absolute, out _),
                    $"{TokenValidationSettings.SectionName}:Staff:Issuer boş ya da mutlak bir adres değil.")
                .Validate(
                    settings => Uri.TryCreate(settings.Staff.StaffAdminUrl, UriKind.Absolute, out _),
                    $"{TokenValidationSettings.SectionName}:Staff:StaffAdminUrl boş ya da mutlak bir adres değil. Çalışanın izinleri olmadan hiçbir çalışan isteği geçemez.");
        }

        // Fail fast: eksik ayar ilk istekte değil, başlangıçta patlasın.
        settings.ValidateOnStart();

        var authentication = services
            .AddAuthentication(acceptStaffTokens ? SelectorScheme : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        ConfigureBearer(services, JwtBearerDefaults.AuthenticationScheme, settings => (settings.Issuer, settings.MetadataAddress));

        if (acceptStaffTokens)
        {
            AddStaffBearer(services, authentication);
            AddStaffAdminPermissions(services);

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

        AddPolicies(services, new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.IsEmployee())
            .Build());

        return services;
    }

    /// <summary>
    /// Yalnızca çalışanların Keycloak'ının token'ı. Müşterinin token'ını hiç tanımayan iç
    /// servis için (personel yönetimi): müşteriyle işi yok, müşteri token'ı 401 alıyor.
    /// Varsayılan politika çalışan istiyor; uçlar ayrıca izin istiyor. İzinlerin kaynağını
    /// (<see cref="IStaffPermissions"/>) servis kendisi kaydediyor.
    /// </summary>
    public static IServiceCollection AddHiWalletStaffAuthentication(this IServiceCollection services)
    {
        services.AddOptions<TokenValidationSettings>()
            .BindConfiguration(TokenValidationSettings.SectionName)
            .Validate(
                settings => Uri.TryCreate(settings.Staff.Issuer, UriKind.Absolute, out _),
                $"{TokenValidationSettings.SectionName}:Staff:Issuer boş ya da mutlak bir adres değil.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.Audience),
                $"{TokenValidationSettings.SectionName}:Audience boş.")
            .ValidateOnStart();

        AddStaffBearer(services, services.AddAuthentication(StaffScheme));

        AddPolicies(services, new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.IsEmployee())
            .Build());

        return services;
    }

    private static void AddStaffBearer(IServiceCollection services, AuthenticationBuilder authentication)
    {
        authentication.AddJwtBearer(StaffScheme);

        ConfigureBearer(services, StaffScheme, settings => (settings.Staff.Issuer, settings.Staff.MetadataAddress));
        services.AddOptions<JwtBearerOptions>(StaffScheme).Configure(jwt =>
            jwt.TokenValidationParameters.AuthenticationType = StaffIdentityType);
    }

    /// <summary>İzinler personel yönetiminden, çalışanın her isteğinde.</summary>
    private static void AddStaffAdminPermissions(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient<IStaffPermissions, StaffAdminPermissions>((provider, http) =>
        {
            var url = provider.GetRequiredService<IOptions<TokenValidationSettings>>().Value.Staff.StaffAdminUrl!;

            http.BaseAddress = new Uri(url.TrimEnd('/') + "/");
            http.Timeout = StaffPermissionsTimeout;
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, StaffPermissionsExceptionHandler>());
    }

    private static void AddPolicies(IServiceCollection services, AuthorizationPolicy fallback)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, StaffPermissionHandler>());

        var authorization = services.AddAuthorizationBuilder()
            .SetFallbackPolicy(fallback)
            .AddPolicy(HiWalletPolicies.CustomerOrStaff, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new StaffPermissionRequirement(StaffPermissions.CustomerView, CustomersPass: true)));

        foreach (var permission in StaffPermissions.All)
        {
            authorization.AddPolicy(HiWalletPolicies.For(permission), policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new StaffPermissionRequirement(permission)));
        }
    }

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

    /// <summary>
    /// Kullanıcının parolasıyla en son giriş yaptığı an (<c>auth_time</c>); token'da yoksa
    /// <c>null</c>. Yenilenen token aynı anı taşıyor: açık kalan oturum taze giriş sayılmıyor.
    /// </summary>
    public static DateTimeOffset? AuthenticatedAt(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst("auth_time")?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>Token çalışanların realm'inden geldi ve orada doğrulandı.</summary>
    public static bool IsEmployee(this ClaimsPrincipal user) =>
        user.Identities.Any(identity => identity is { IsAuthenticated: true, AuthenticationType: StaffIdentityType });
}

/// <summary>
/// Çalışanın izinleri. Kod yalnızca bunları tanıyor; roller panelde bu izinlerden
/// kuruluyor ve personel yönetiminin veritabanında duruyor. Yeni bir yetki türü yeni bir
/// izin ve kod değişikliği demek.
/// </summary>
public static class StaffPermissions
{
    /// <summary>Müşteri kaydını görüntüler: hesap, cüzdan, hareket, promo partisi, çekim.</summary>
    public const string CustomerView = "customer.view";

    /// <summary>İncelemedeki çekimi serbest bırakır ya da iptal eder.</summary>
    public const string WithdrawalReview = "withdrawal.review";

    /// <summary>Müşteriye personel promo'su verir.</summary>
    public const string PromoGrant = "promo.grant";

    /// <summary>Promo kampanyalarını görüntüler.</summary>
    public const string CampaignView = "campaign.view";

    /// <summary>Promo kampanyası açar ve bitirir.</summary>
    public const string CampaignManage = "campaign.manage";

    /// <summary>İşyerinin platform fonlu promo kabulünü değiştirir.</summary>
    public const string MerchantPromoAcceptance = "merchant.promo_acceptance";

    /// <summary>Cüzdana geçirilemeyip askıya alınan havaleleri görüntüler.</summary>
    public const string DepositView = "deposit.view";

    /// <summary>Personeli, rolleri ve rollerin izinlerini yönetir.</summary>
    public const string StaffManage = "staff.manage";

    /// <summary>Panelde izin seçilirken gösterilen açıklamalar; her iznin bir açıklaması var.</summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [CustomerView] = "Müşteri kaydını görüntüler: hesap, cüzdan, hareket, promo partisi, çekim",
        [WithdrawalReview] = "İncelemedeki çekimi serbest bırakır ya da iptal eder",
        [PromoGrant] = "Müşteriye personel promo'su verir",
        [CampaignView] = "Promo kampanyalarını görüntüler",
        [CampaignManage] = "Promo kampanyası açar ve bitirir",
        [MerchantPromoAcceptance] = "İşyerinin platform fonlu promo kabulünü değiştirir",
        [DepositView] = "Cüzdana geçirilemeyip askıya alınan havaleleri görüntüler",
        [StaffManage] = "Personeli, rolleri ve rollerin izinlerini yönetir"
    };

    public static readonly IReadOnlyList<string> All = [.. Descriptions.Keys];
}

public static class HiWalletPolicies
{
    /// <summary>
    /// Müşteri kendi kaynağını, çalışan <see cref="StaffPermissions.CustomerView"/> izniyle her
    /// müşterinin kaynağını görüntülüyor. Yalnızca okuma uçlarında; müşterinin para
    /// hareketi başlatan uçları çalışana kapalı.
    /// </summary>
    public const string CustomerOrStaff = "customer-or-staff";

    private const string Prefix = "staff:";

    /// <summary>Çalışanların Keycloak'ından ve şu an bu izinle; müşterinin token'ı geçmiyor.</summary>
    public const string CustomerView = Prefix + StaffPermissions.CustomerView;

    public const string WithdrawalReview = Prefix + StaffPermissions.WithdrawalReview;

    public const string PromoGrant = Prefix + StaffPermissions.PromoGrant;

    public const string CampaignView = Prefix + StaffPermissions.CampaignView;

    public const string CampaignManage = Prefix + StaffPermissions.CampaignManage;

    public const string MerchantPromoAcceptance = Prefix + StaffPermissions.MerchantPromoAcceptance;

    public const string DepositView = Prefix + StaffPermissions.DepositView;

    public const string StaffManage = Prefix + StaffPermissions.StaffManage;

    public static string For(string permission) => Prefix + permission;
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

        /// <summary>
        /// Personel yönetiminin (<c>staff-admin</c>) adresi: çalışanın izinleri her istekte
        /// buradan okunuyor. Yalnızca çalışan token'ını kabul eden iç servislerde; personel
        /// yönetiminin kendisi izinleri kendi veritabanından okuyor.
        /// </summary>
        public string? StaffAdminUrl { get; set; }
    }
}
