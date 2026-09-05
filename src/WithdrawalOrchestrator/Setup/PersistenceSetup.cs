using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WithdrawalOrchestrator.Setup;

public static class PersistenceSetup
{
    /// <summary>Bu servisin kendi veritabanı. Wallet şemasına erişimi yok.</summary>
    public const string ConnectionStringName = "Withdrawal";

    public static IServiceCollection AddOrchestratorPersistence(this IServiceCollection services)
    {
        // Yalnızca factory: context'e ihtiyaç duyan yerlerin çoğu istek kapsamı
        // DIŞINDA (outbox relay, takılmış saga taraması) ve hepsinin kendi ömrünü
        // yönetmesi tek kalıp bırakıyor.
        //
        // Bağlantı dizesi KAYIT anında değil, context kurulurken okunuyor —
        // WebApplicationFactory konfigürasyonunu host kurulduktan sonra ekliyor.
        services.AddDbContextFactory<OrchestratorDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            options.UseNpgsql(configuration.GetConnectionString(ConnectionStringName));
        });

        return services;
    }
}
