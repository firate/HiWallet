using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HiWallet.Shared.Infrastructure.Authentication;

/// <summary>
/// Kimlik sağlayıcının imzaladığı token'ı doğrular. Ön API'ler de iç servisler de bu
/// kurulumu kullanıyor: ön API token'ı iç servise AYNEN iletiyor ve iç servis onu
/// yeniden doğruluyor. Ön API'ye körü körüne güvenilmiyor; ele geçirilmiş bir ön API
/// başkası adına istek yazdıramıyor.
///
/// Varsayılan politika kimlik istiyor: yeni bir uç kendiliğinden korumalı açılıyor.
/// Açık kalması gereken uç (sağlık, API dokümanı) bunu kendisi söylüyor.
/// </summary>
public static class AuthenticationSetup
{
    public static IServiceCollection AddHiWalletAuthentication(this IServiceCollection services)
    {
        services.AddOptions<TokenValidationSettings>()
            .BindConfiguration(TokenValidationSettings.SectionName)
            .Validate(
                settings => Uri.TryCreate(settings.Issuer, UriKind.Absolute, out _),
                $"{TokenValidationSettings.SectionName}:Issuer boş ya da mutlak bir adres değil. Token'ı kimin imzaladığı bilinmeden doğrulanamaz.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.Audience),
                $"{TokenValidationSettings.SectionName}:Audience boş.")
            // Fail fast: eksik ayar ilk istekte değil, başlangıçta patlasın.
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<TokenValidationSettings>>((jwt, options) =>
            {
                var settings = options.Value;

                jwt.Authority = settings.Issuer;
                jwt.Audience = settings.Audience;
                jwt.RequireHttpsMetadata = settings.RequireHttpsMetadata;

                // Issuer token'daki adres, yani istemcinin kimlik sağlayıcıya ulaştığı
                // public adres. İç ağdaki servis anahtarları başka bir adresten okuyabiliyor.
                if (!string.IsNullOrWhiteSpace(settings.MetadataAddress))
                {
                    jwt.MetadataAddress = settings.MetadataAddress;
                }

                // Claim adları token'daki gibi kalıyor: "sub" .NET'in uzun URI'sine
                // çevrilmiyor.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.NameClaimType = SubjectClaim;

                // Ön API token'ı iç servise iletiyor; iletirken buradan okuyor.
                jwt.SaveToken = true;
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    public const string SubjectClaim = "sub";

    /// <summary>Kimlik sağlayıcıdaki kullanıcı kimliği. Doğrulanmış token'da her zaman var.</summary>
    public static string Subject(this ClaimsPrincipal user) =>
        user.FindFirst(SubjectClaim)?.Value
        ?? throw new InvalidOperationException("Token'da 'sub' yok.");
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
}
