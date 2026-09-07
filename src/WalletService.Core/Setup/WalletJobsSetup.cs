using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.WalletService.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HiWallet.WalletService.Setup;

public static class WalletJobsSetup
{
    /// <summary>
    /// Wallet sınırının zamanlanmış işleri. Kayıt Core'da çünkü job tipleri
    /// <c>internal</c> — host'a açılmaları için tek sebep DI kaydı olurdu ve o
    /// sebep bu extension ile ortadan kalkıyor. <c>PersistenceSetup</c> ve
    /// <c>PoliciesSetup</c> ile aynı kalıp.
    ///
    /// Şu an tek çağıran <c>wallet-consumer</c>: job'ların ingress'i yok ve aynı
    /// ledger'a yazıyorlar, yani ayrı bir deployable açmanın gerekçesi yok
    /// (<c>decisions.md</c> madde 28). <c>wallet-api</c> BU METODU ÇAĞIRMAMALI —
    /// public bir uygulamada arka plan toplu işi koşturmak, istek trafiğiyle
    /// raporlamayı aynı sürecin kaynaklarına bağlar.
    /// </summary>
    public static IServiceCollection AddWalletJobs(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BusinessSummaryOptions>(
            configuration.GetSection(BusinessSummaryOptions.SectionName));

        // Bağımlılığı getiren job, o yüzden kaydı da burada. TryAdd: host zaten
        // kaydettiyse (ya da test sahte bir saat verdiyse) üzerine YAZMIYOR.
        services.TryAddSingleton(TimeProvider.System);

        services.AddHiWalletJobLease(PersistenceSetup.ConnectionStringName);
        services.AddSingleton<BusinessSummaryCalculator>();
        services.AddHostedService<BusinessSummaryJob>();

        return services;
    }
}
