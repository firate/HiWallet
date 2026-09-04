namespace HiWallet.WalletService.Setup;

/// <summary>
/// Fail fast: kritik config eksikse uygulama ayağa kalkmadan patlar (baseline.md madde 1).
///
/// Host KURULDUKTAN sonra çalışır, kayıt anında değil — konfigürasyonun tüm kaynakları
/// (env, User Secrets, test override'ları) o noktada birleşmiş oluyor.
/// </summary>
public static class ConfigValidation
{
    public static WebApplication ValidateHiWalletConfiguration(this WebApplication app)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(
                app.Configuration.GetConnectionString(PersistenceSetup.ConnectionStringName)))
        {
            missing.Add($"ConnectionStrings:{PersistenceSetup.ConnectionStringName}");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Zorunlu konfigürasyon eksik: {string.Join(", ", missing)}. " +
                "Local'de User Secrets, container'da .env üzerinden verilir.");
        }

        return app;
    }
}
