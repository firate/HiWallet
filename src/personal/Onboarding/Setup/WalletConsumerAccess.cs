using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace HiWallet.Onboarding.Setup;

/// <summary>
/// wallet-consumer'ın sorusu: bir kimlik numarasının bir hesap sahibine ait olup olmadığı.
/// Çağıran müşteri değil servisin kendisi; token'ı kendi istemcisinin kimlik bilgileriyle
/// alıyor (client credentials) ve istemcinin adı <c>azp</c>'de. Bu bir kimlik numarası
/// sorgusu: istemci adı sabit, başka hiçbir token geçmiyor.
/// </summary>
public static class WalletConsumerAccess
{
    public const string Policy = "WalletConsumer";

    private const string ClientIdKey = "Authentication:WalletConsumerClientId";

    public static IServiceCollection AddWalletConsumerAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var clientId = configuration[ClientIdKey];

        // Fail fast: boş bir istemci adı politikayı ya herkese kapatır ya da hiç kimseyi
        // tanımaz; ikisi de ilk havalede ortaya çıkardı.
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException($"Zorunlu konfigürasyon eksik: {ClientIdKey}.");
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .RequireClaim("azp", clientId)
                .RequireAssertion(context => !context.User.IsEmployee()));

        return services;
    }
}
