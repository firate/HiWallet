using HiWallet.BankIntegration.Setup;
using HiWallet.BankWebhook.Application;
using HiWallet.Shared.Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.BankWebhook.Setup;

public static class BankWebhookSetup
{
    public static IServiceCollection AddBankWebhook(this IServiceCollection services)
    {
        services.AddBankPersistence();

        services.AddScoped<BankCallbackWriter>();

        // Secret'lar KAYIT anında değil, ilk çözümlemede okunuyor. Kayıt anında
        // okunsaydı WebApplicationFactory'nin host kurulduktan SONRA eklediği
        // konfigürasyon hiç görülmezdi.
        services.AddSingleton(provider => BuildSecrets(provider.GetRequiredService<IConfiguration>()));

        services.AddHealthChecks()
            // RabbitMQ kontrolü YOK ve bu kasıtlı: bu servisin broker'a hiç
            // bağlantısı yok. Callback'i kabul etmek yalnızca Postgres'e bağlı —
            // inbox'ın varlık sebebi tam olarak bu (topup-webhook ile aynı gerekçe).
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(BankPersistenceSetup.ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3));

        return services;
    }

    /// <summary>Fail fast (baseline.md madde 1). Host KURULDUKTAN sonra çalışır.</summary>
    public static WebApplication ValidateBankWebhookConfiguration(this WebApplication app)
    {
        BankPersistenceSetup.RequireConnectionString(app.Configuration);

        // Secret'ları burada çözümlemek fail-fast'i geri getiriyor: eksik secret
        // ilk callback'te değil, başlangıçta patlıyor.
        var secrets = app.Services.GetRequiredService<BankSecrets>();

        app.Logger.LogInformation(
            "Callback doğrulaması tanımlı kurumlar: {Banks}", string.Join(", ", secrets.Banks));

        return app;
    }

    private static BankSecrets BuildSecrets(IConfiguration configuration)
    {
        var secrets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var bank in configuration.GetSection(BankSecrets.SectionName).GetChildren())
        {
            var configured = ReadBankSecrets(bank);

            // Boş secret'la kayıtlı bir kurum sessizce "her imzayı reddet"e
            // dönüşürdü; onun yerine patlıyor (baseline.md madde 1).
            if (configured.Count == 0 || configured.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"{BankSecrets.SectionName}:{bank.Key} için callback secret'ı boş. " +
                    "Tek secret CallbackSecret, rotasyon penceresinde CallbackSecrets:0, :1 " +
                    "ile verilir. Ortam değişkeni ya da User Secrets kullanılır, " +
                    "appsettings.json'a yazılmaz.");
            }

            secrets[bank.Key] = configured;
        }

        if (secrets.Count == 0)
        {
            throw new InvalidOperationException(
                $"Hiç kurum tanımlı değil ({BankSecrets.SectionName}). " +
                "Secret'ı olmayan bir callback servisi her isteği reddeder ve bu " +
                "sessizce mutabakat taramasının tek yol olmasına yol açardı.");
        }

        return new BankSecrets(secrets);
    }

    /// <summary>
    /// Rotasyon penceresinde birden fazla secret geçerli:
    /// <c>Banks__banka__CallbackSecrets__0</c>, <c>__1</c>. Sıra korunuyor, ilki
    /// güncel olan. Tek secret'lık <c>CallbackSecret</c> biçimi de duruyor —
    /// rotasyon yokken tek değere sıra numarası eklemek gereksiz.
    /// </summary>
    private static List<string> ReadBankSecrets(IConfigurationSection bank)
    {
        var rotated = bank.GetSection("CallbackSecrets")
            .GetChildren()
            .Select(secret => secret.Value ?? string.Empty)
            .ToList();

        if (rotated.Count > 0) return rotated;

        var single = bank["CallbackSecret"];

        return single is null ? [] : [single];
    }
}
