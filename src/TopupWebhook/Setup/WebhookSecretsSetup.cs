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
        var secrets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var provider in configuration.GetSection(WebhookSecrets.SectionName).GetChildren())
        {
            var configured = ReadProviderSecrets(provider);

            // Boş secret'la kayıtlı bir sağlayıcı sessizce "her imzayı reddet"e
            // dönüşürdü; onun yerine patlıyor (baseline.md madde 1).
            if (configured.Count == 0 || configured.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"{WebhookSecrets.SectionName}:{provider.Key} için webhook secret'ı boş. " +
                    "Tek secret WebhookSecret, rotasyon penceresinde WebhookSecrets:0, :1 " +
                    "ile verilir. Ortam değişkeni ya da User Secrets kullanılır, " +
                    "appsettings.json'a yazılmaz.");
            }

            secrets[provider.Key] = configured;
        }

        if (secrets.Count == 0)
        {
            throw new InvalidOperationException(
                $"Hiç sağlayıcı tanımlı değil ({WebhookSecrets.SectionName}). " +
                "Secret'ı olmayan bir webhook servisi her isteği reddeder.");
        }

        return new WebhookSecrets(secrets);
    }

    /// <summary>
    /// Rotasyon penceresinde birden fazla secret geçerli:
    /// <c>Providers__saglayici__WebhookSecrets__0</c>, <c>__1</c>. Sıra korunuyor,
    /// ilki güncel olan. Tek secret'lık <c>WebhookSecret</c> biçimi de duruyor —
    /// rotasyon yokken tek değere sıra numarası eklemek gereksiz.
    /// </summary>
    private static List<string> ReadProviderSecrets(IConfigurationSection provider)
    {
        var rotated = provider.GetSection("WebhookSecrets")
            .GetChildren()
            .Select(secret => secret.Value ?? string.Empty)
            .ToList();

        if (rotated.Count > 0) return rotated;

        var single = provider["WebhookSecret"];

        return single is null ? [] : [single];
    }
}
