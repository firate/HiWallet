using HiWallet.TopupWebhook.Application;

namespace HiWallet.TopupWebhook.Setup;

/// <summary>
/// Fail fast: kritik config eksikse uygulama ayağa kalkmadan patlar (baseline.md madde 1).
///
/// Host KURULDUKTAN sonra çalışır, kayıt anında değil — konfigürasyonun tüm kaynakları
/// (env, User Secrets, test override'ları) o noktada birleşmiş oluyor.
/// </summary>
public static class ConfigValidation
{
    public static WebApplication ValidateTopupConfiguration(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(
                app.Configuration.GetConnectionString(PersistenceSetup.ConnectionStringName)))
        {
            throw new InvalidOperationException(
                $"Zorunlu konfigürasyon eksik: ConnectionStrings:{PersistenceSetup.ConnectionStringName}. " +
                "Local'de User Secrets, container'da .env üzerinden verilir.");
        }

        // Çözümlemenin kendisi doğrulama: eksik ya da boş secret burada patlıyor.
        _ = app.Services.GetRequiredService<WebhookSecrets>();

        return app;
    }
}
