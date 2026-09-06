using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HiWallet.Shared.Infrastructure.Jobs;

public static class JobsSetup
{
    /// <param name="connectionStringName">
    /// Kilidin tutulacağı veritabanı. Her servis KENDİ veritabanını veriyor: kilit
    /// servisler arası değil, aynı servisin instance'ları arasında. Ortak bir kilit
    /// veritabanı, servis sınırını (madde 7) delen bir bağımlılık olurdu.
    /// </param>
    /// <remarks>
    /// Bağlantı dizesi KAYIT anında değil, ilk çözümlemede okunuyor — persistence
    /// kurulumundaki gerekçenin aynısı: <c>WebApplicationFactory</c> konfigürasyonu
    /// host kurulduktan SONRA ekliyor, kayıt anında okunsaydı testler bunu göremezdi.
    /// </remarks>
    public static IServiceCollection AddHiWalletJobLease(
        this IServiceCollection services, string connectionStringName)
    {
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(connectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{connectionStringName} boş; job kilidi kurulamıyor. " +
                    "Kilitsiz koşan bir job, servis ölçeklendiğinde sessizce iki kez çalışır.");
            }

            return new JobLease(connectionString, provider.GetRequiredService<ILogger<JobLease>>());
        });

        return services;
    }
}
