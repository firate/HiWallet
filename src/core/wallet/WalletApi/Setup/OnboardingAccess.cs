using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace HiWallet.WalletApi.Setup;

/// <summary>
/// Onboarding servisinin uçları: bireysel hesabı açmak ve doğrulama seviyesini yükseltmek.
/// Çağıran müşteri değil servisin kendisi. Token'ı kendi istemcisinin kimlik bilgileriyle
/// alıyor (client credentials) ve istemcinin adı token'ın <c>azp</c>'sinde; imzalı token'da
/// bu adı yalnızca o istemcinin gizli anahtarını bilen yazdırabiliyor.
///
/// Kayıt anında müşterinin henüz token'ı yok; hesabın kime açılacağını (<c>holder</c>)
/// onboarding gövdede söylüyor. Kimliğin başlıkla taşınmadığı kuralının istisnası değil:
/// yetki onboarding'in token'ından, iddia yalnızca bu uçlarda kabul ediliyor.
/// </summary>
public static class OnboardingAccess
{
    public const string Policy = "Onboarding";

    private const string ClientIdKey = "Authentication:OnboardingClientId";

    public static IServiceCollection AddOnboardingAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var clientId = configuration[ClientIdKey];

        // Fail fast: boş bir istemci adı politikayı ya herkese kapatır ya da hiç kimseyi
        // tanımaz; ikisi de ilk kayıtta ortaya çıkardı.
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException($"Zorunlu konfigürasyon eksik: {ClientIdKey}.");
        }

        // İstemci müşterinin realm'inde. Çalışanların realm'inde aynı adla açılmış bir
        // istemci onun yerine geçmiyor: realm'i token'ın içeriği değil onu doğrulayan
        // şema söylüyor.
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .RequireClaim("azp", clientId)
                .RequireAssertion(context => !context.User.IsEmployee()));

        return services;
    }
}
