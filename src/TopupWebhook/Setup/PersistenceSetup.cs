using HiWallet.TopupWebhook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.TopupWebhook.Setup;

public static class PersistenceSetup
{
    /// <summary>Bu servisin kendi veritabanı. Wallet şemasına erişimi yok.</summary>
    public const string ConnectionStringName = "Topup";

    public static IServiceCollection AddTopupPersistence(this IServiceCollection services)
    {
        // Bağlantı dizesi KAYIT anında değil, context kurulurken okunuyor —
        // WebApplicationFactory konfigürasyonunu host kurulduktan sonra ekliyor.
        services.AddDbContextFactory<InboxDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            options.UseNpgsql(configuration.GetConnectionString(ConnectionStringName));
        });

        return services;
    }
}
