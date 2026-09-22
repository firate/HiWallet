using HiWallet.BankIntegration.Persistence;
using Microsoft.EntityFrameworkCore;
using HiWallet.Shared.Infrastructure.Persistence;

namespace HiWallet.BankIntegration.Setup;

/// <summary>
/// Şema erişimi. İki host da bunu çağırıyor — <c>bank-adapter</c> ve
/// <c>bank-webhook</c> aynı veritabanına, aynı modelle bağlanıyor.
/// </summary>
public static class BankPersistenceSetup
{
    public const string ConnectionStringName = "Bank";

    public static IServiceCollection AddBankPersistence(this IServiceCollection services)
    {
        // Bağlantı dizesi KAYIT anında değil, context kurulurken okunuyor —
        // WebApplicationFactory konfigürasyonunu host kurulduktan SONRA ekliyor.
        services.AddDbContextFactory<BankDbContext>((provider, options) =>
            options.UseNpgsql(
                provider.GetRequiredService<IConfiguration>()
                    .GetConnectionString(ConnectionStringName),
                npgsql => npgsql.CommandTimeout(DbTimeouts.CommandSeconds)));

        return services;
    }

    /// <summary>
    /// Fail fast (baseline.md madde 1). Host KURULDUKTAN sonra çağrılır.
    /// </summary>
    public static string RequireConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Zorunlu konfigürasyon eksik: ConnectionStrings:{ConnectionStringName}. " +
                "Local'de User Secrets, container'da .env üzerinden verilir.");
        }

        return connectionString;
    }
}
