using HiWallet.TopupWebhook.Application;

namespace HiWallet.TopupWebhook.Setup;

public static class WebhookSecretsSetup
{
    /// <summary>
    /// Secret'lar KAYIT anında değil, ilk çözümlemede okunuyor. Kayıt anında
    /// okunsaydı <c>WebApplicationFactory</c>'nin host kurulduktan SONRA eklediği
    /// konfigürasyon hiç görülmezdi — wallet-service'te aynı hata bağlantı dizesinde
    /// yaşandı ve testler sessizce gerçek veritabanına gitti.
    ///
    /// Fail-fast kaybolmuyor: <see cref="ConfigValidation"/> host kurulduktan hemen
    /// sonra bu servisi çözümleyip patlatıyor.
    /// </summary>
    public static IServiceCollection AddWebhookSecrets(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
            Build(provider.GetRequiredService<IConfiguration>()));

        return services;
    }

    private static WebhookSecrets Build(IConfiguration configuration)
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var provider in configuration.GetSection(WebhookSecrets.SectionName).GetChildren())
        {
            var secret = provider["WebhookSecret"];

            // Boş secret'la kayıtlı bir sağlayıcı sessizce "her imzayı reddet"e
            // dönüşürdü; onun yerine patlıyor (baseline.md madde 1).
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    $"{WebhookSecrets.SectionName}:{provider.Key}:WebhookSecret boş. " +
                    "Ortam değişkeni ya da User Secrets ile verilir, appsettings.json'a yazılmaz.");
            }

            secrets[provider.Key] = secret;
        }

        if (secrets.Count == 0)
        {
            throw new InvalidOperationException(
                $"Hiç sağlayıcı tanımlı değil ({WebhookSecrets.SectionName}). " +
                "Secret'ı olmayan bir webhook servisi her isteği reddeder.");
        }

        return new WebhookSecrets(secrets);
    }
}
