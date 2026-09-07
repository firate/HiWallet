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

    /// <summary>
    /// topup-webhook da aynı adı kullanıyor ama başka bir anahtar için
    /// (<c>WebhookSecret</c>). Aynı bölüm adı bilinçli: sağlayıcı kayıt defteri tek
    /// kavram, iki servis ondan farklı alanları okuyor. Ayrı adlar olsaydı yeni bir
    /// sağlayıcı eklerken iki yerden birini atlamak sessiz kalırdı.
    /// </summary>
    private const string ProvidersSection = "Providers";

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
    /// Sağlayıcı ücret tarifeleri. Çekim tarifesiyle aynı gerekçeyle AYRI bir çağrı:
    /// sağlayıcı ücreti yalnızca top-up'ı işleyen uygulamayı (wallet-consumer)
    /// ilgilendiriyor, wallet-api'nin olmayan bir bölümü istemesi anlamsız olurdu.
    ///
    /// Bölüm eksikse PATLIYOR. Sessizce "ücretsiz"e düşseydi her sağlayıcının gideri
    /// raporlardan silinir ve fatura geldiğinde "beklenen toplam sıfır" ile
    /// uyuşmazlık üretirdi (madde 11).
    /// </summary>
    public static IServiceCollection AddProviderPolicy(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ProvidersSection);
        var terms = new Dictionary<string, ProviderTerms>(StringComparer.Ordinal);

        foreach (var child in section.GetChildren())
        {
            var options = new ProviderOptions();
            child.Bind(options);

            if (!Enum.TryParse<FeeSettlement>(options.FeeSettlement, ignoreCase: true, out var model))
            {
                throw new InvalidOperationException(
                    $"{ProvidersSection}:{child.Key}:FeeSettlement geçersiz: '{options.FeeSettlement}'. " +
                    $"Geçerli değerler: {string.Join(", ", Enum.GetNames<FeeSettlement>())}");
            }

            terms[child.Key] = new ProviderTerms(
                child.Key, model, new ProviderFeeTariff(options.Fee.Rate, options.Fee.Fixed));
        }

        if (terms.Count == 0)
        {
            throw new InvalidOperationException(
                $"Zorunlu konfigürasyon eksik: {ProvidersSection}. Sağlayıcı tarifesi " +
                "varsayılana bırakılmaz — ücretsiz sayılan bir sağlayıcı gideri gizler.");
        }

        services.AddSingleton(new ProviderPolicy(terms));

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

    private sealed class ProviderOptions
    {
        /// <summary>
        /// Varsayılan YOK: boş bırakıldığında <c>Enum.TryParse</c> patlıyor. Bir
        /// varsayılan verilseydi eksik ayar sessizce o modele düşerdi ve model
        /// ledger'a ne yazılacağını belirliyor.
        /// </summary>
        public string FeeSettlement { get; init; } = string.Empty;

        public FeeOptions Fee { get; init; } = new();
    }

    private sealed class FeeOptions
    {
        public decimal Rate { get; init; }

        public decimal Fixed { get; init; }
    }
}
