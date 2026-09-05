using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletService.Setup;

/// <summary>
/// Limit ve komisyon tarifeleri konfigürasyondan gelir; Domain <c>IConfiguration</c>
/// tanımaz, bağlama burada yapılır.
/// </summary>
public static class PoliciesSetup
{
    private const string LimitsSection = "Transfers:Limits";
    private const string CommissionsSection = "Transfers:Commissions";
    private const string WithdrawalSection = "Withdrawals";

    public static IServiceCollection AddHiWalletPolicies(
        this IServiceCollection services, IConfiguration configuration)
    {
        var limits = Bind<TransferLimitOptions>(configuration, LimitsSection)
            .ToDictionary(kv => kv.Key, kv => new TransferLimit(kv.Value.PerTransaction, kv.Value.Daily));

        var commissions = Bind<CommissionRateOptions>(configuration, CommissionsSection)
            .ToDictionary(
                kv => kv.Key,
                kv => new CommissionRate(kv.Value.Rate, kv.Value.Minimum, kv.Value.Maximum));

        // Tarifeler çalışma anında değişmiyor; singleton yeterli ve her istekte yeniden
        // sözlük kurmaktan iyi.
        services.AddSingleton(new LimitPolicy(limits));
        services.AddSingleton(new CommissionPolicy(commissions));

        return services;
    }

    /// <summary>
    /// Çekim tarifesi AYRI bir çağrı, transfer tarifeleriyle birlikte kaydedilmiyor:
    /// çekimi yalnızca wallet-consumer işliyor ve tarifenin yalnızca orada zorunlu
    /// olması gerekiyor. Birlikte kaydedilseydi wallet-api de olmayan bir bölümü
    /// istemek zorunda kalırdı.
    ///
    /// Konfigürasyon <c>Transfers</c> altında DEĞİL: çekim bir transfer değil ve oraya
    /// konsaydı <c>TransferType</c> anahtarlı bağlama onu tanımayıp patlardı.
    /// </summary>
    public static IServiceCollection AddWithdrawalPolicy(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(WithdrawalSection);

        // Bölüm yoksa PATLIYOR, sessizce "komisyonsuz ve limitsiz"e düşmüyor.
        // Transfer tarafındaki "tarifesi tanımlı olmayan tip serbest" kuralı burada
        // geçerli değil: orada tip başına tarife var ve bazılarının olmaması normal,
        // burada tek tarife var ve yokluğu yapılandırma hatasıdır. Sessizce
        // düşseydi üretimde her çekim komisyonsuz ve limitsiz işlenirdi.
        if (!section.GetChildren().Any())
        {
            throw new InvalidOperationException(
                $"Zorunlu konfigürasyon eksik: {WithdrawalSection}:Commission ve " +
                $"{WithdrawalSection}:Limit. Çekim tarifesi varsayılana bırakılmaz.");
        }

        var commission = new CommissionRateOptions();
        section.GetSection("Commission").Bind(commission);

        var limit = new TransferLimitOptions();
        section.GetSection("Limit").Bind(limit);

        services.AddSingleton(new WithdrawalPolicy(
            new CommissionRate(commission.Rate, commission.Minimum, commission.Maximum),
            new TransferLimit(limit.PerTransaction, limit.Daily)));

        return services;
    }

    /// <summary>
    /// Bölümü <c>TransferType</c> anahtarlı sözlüğe bağlar. Tanınmayan bir anahtar
    /// startup'ta patlar — konfigürasyondaki yazım hatası sessizce "tarife yok"a
    /// dönüşmemeli (baseline.md madde 1, fail fast).
    /// </summary>
    private static Dictionary<TransferType, T> Bind<T>(IConfiguration configuration, string section)
        where T : new()
    {
        var result = new Dictionary<TransferType, T>();

        foreach (var child in configuration.GetSection(section).GetChildren())
        {
            if (!Enum.TryParse<TransferType>(child.Key, ignoreCase: true, out var type))
            {
                throw new InvalidOperationException(
                    $"{section}:{child.Key} bilinmeyen bir transfer tipi. " +
                    $"Geçerli değerler: {string.Join(", ", Enum.GetNames<TransferType>())}");
            }

            var options = new T();
            child.Bind(options);
            result[type] = options;
        }

        return result;
    }

    private sealed class TransferLimitOptions
    {
        public decimal? PerTransaction { get; init; }

        public decimal? Daily { get; init; }
    }

    private sealed class CommissionRateOptions
    {
        public decimal Rate { get; init; }

        public decimal? Minimum { get; init; }

        public decimal? Maximum { get; init; }
    }
}
