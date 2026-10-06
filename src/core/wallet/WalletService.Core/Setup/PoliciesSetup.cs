using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
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
    private const string KycLimitsSection = "Kyc:MonthlyLimits";
    private const string KycBalanceCapsSection = "Kyc:BalanceCaps";

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

        // Tarifeler çalışma anında değişmiyor; singleton yeterli ve her request'te yeniden
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
        // düşseydi canlıda her çekim komisyonsuz ve limitsiz işlenirdi.
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

            // Kova da sağlayıcı bazında ve varsayılanı YOK (decisions.md madde 36):
            // kovası yazılmamış bir kart sağlayıcısının parası sessizce cash sayılsaydı
            // IBAN'a çıkabilir hale gelirdi.
            if (!Enum.TryParse<FundType>(options.FundType, ignoreCase: true, out var fundType))
            {
                throw new InvalidOperationException(
                    $"{ProvidersSection}:{child.Key}:FundType geçersiz: '{options.FundType}'. " +
                    $"Geçerli değerler: {string.Join(", ", Enum.GetNames<FundType>())}");
            }

            terms[child.Key] = new ProviderTerms(
                child.Key, model, new ProviderFeeTariff(options.Fee.Rate, options.Fee.Fixed), fundType);
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
    /// Doğrulama seviyesine göre aylık limitler ve bakiye tavanı (<see cref="KycLimitPolicy"/>).
    /// Her uygulama yalnızca kendi uyguladığı hareketleri istiyor: wallet-api transferleri ve
    /// ödemeyi, wallet-consumer çekimi ve havaleyi. Çekim tarifesiyle aynı gerekçe: bölüm tek
    /// yerde durmalı, iki kopya ayrıştığında müşteri beklediğinden farklı limitle karşılaşırdı.
    ///
    /// İstisna hesaba gelen taraf: ayın toplam girişi (<see cref="KycMovement.IncomingTotal"/>)
    /// ve bakiye tavanı hem gelen transferde hem havalede sayılıyor, ikisi de iki
    /// uygulamada yazılı. Gelen bir hareket isteyen uygulamaya ikisi kendiliğinden zorunlu.
    ///
    /// Her seviye ve istenen her hareket yazılmış olmak zorunda; eksikse PATLIYOR.
    /// Politika eksik satırı kapalı sayıyor ama bir yazım hatası bir seviyeyi sessizce
    /// kapatmamalı. Tanınmayan seviye ya da hareket adı da patlıyor. Kimliği tespit
    /// edilmemiş seviyenin bakiye tavanı eksikse de patlıyor: tavansız kalması yasal
    /// sınırın sessizce kalkması olurdu.
    /// </summary>
    public static IServiceCollection AddKycLimits(
        this IServiceCollection services, IConfiguration configuration, params KycMovement[] movements)
    {
        var limits = new Dictionary<KycLevel, IReadOnlyDictionary<KycMovement, decimal>>();

        foreach (var levelSection in configuration.GetSection(KycLimitsSection).GetChildren())
        {
            if (!Enum.TryParse<KycLevel>(levelSection.Key, ignoreCase: false, out var level))
            {
                throw new InvalidOperationException(
                    $"{KycLimitsSection}:{levelSection.Key} bilinmeyen bir seviye. " +
                    $"Geçerli değerler: {string.Join(", ", Enum.GetNames<KycLevel>())}");
            }

            var perMovement = new Dictionary<KycMovement, decimal>();

            foreach (var movementSection in levelSection.GetChildren())
            {
                if (!Enum.TryParse<KycMovement>(movementSection.Key, ignoreCase: false, out var movement))
                {
                    throw new InvalidOperationException(
                        $"{KycLimitsSection}:{level}:{movementSection.Key} bilinmeyen bir hareket. " +
                        $"Geçerli değerler: {string.Join(", ", Enum.GetNames<KycMovement>())}");
                }

                perMovement[movement] = movementSection.Get<decimal>();
            }

            limits[level] = perMovement;
        }

        var incoming = movements.Contains(KycMovement.IncomingTransfer) || movements.Contains(KycMovement.Deposit);

        if (incoming && !movements.Contains(KycMovement.IncomingTotal))
        {
            movements = [.. movements, KycMovement.IncomingTotal];
        }

        var missing = Enum.GetValues<KycLevel>()
            .SelectMany(level => movements
                .Where(movement => !limits.TryGetValue(level, out var perMovement) || !perMovement.ContainsKey(movement))
                .Select(movement => $"{KycLimitsSection}:{level}:{movement}"))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Zorunlu konfigürasyon eksik: {string.Join(", ", missing)}. Seviye limiti varsayılana bırakılmaz.");
        }

        var balanceCaps = BindBalanceCaps(configuration);

        if (incoming)
        {
            var uncapped = Enum.GetValues<KycLevel>()
                .Where(level => !level.IsIdentified() && !balanceCaps.ContainsKey(level))
                .Select(level => $"{KycBalanceCapsSection}:{level}")
                .ToList();

            if (uncapped.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Zorunlu konfigürasyon eksik: {string.Join(", ", uncapped)}. " +
                    "Kimliği tespit edilmemiş seviyenin bakiye tavanı varsayılana bırakılmaz.");
            }
        }

        services.AddSingleton(new KycLimitPolicy(limits, balanceCaps));

        return services;
    }

    private static Dictionary<KycLevel, decimal> BindBalanceCaps(IConfiguration configuration)
    {
        var caps = new Dictionary<KycLevel, decimal>();

        foreach (var section in configuration.GetSection(KycBalanceCapsSection).GetChildren())
        {
            if (!Enum.TryParse<KycLevel>(section.Key, ignoreCase: false, out var level))
            {
                throw new InvalidOperationException(
                    $"{KycBalanceCapsSection}:{section.Key} bilinmeyen bir seviye. " +
                    $"Geçerli değerler: {string.Join(", ", Enum.GetNames<KycLevel>())}");
            }

            caps[level] = section.Get<decimal>();
        }

        return caps;
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

        /// <summary>
        /// Aynı gerekçeyle varsayılanı YOK: bu sağlayıcıdan gelen paranın hangi
        /// kovaya düşeceğini belirliyor (decisions.md madde 36).
        /// </summary>
        public string FundType { get; init; } = string.Empty;

        public FeeOptions Fee { get; init; } = new();
    }

    private sealed class FeeOptions
    {
        public decimal Rate { get; init; }

        public decimal Fixed { get; init; }
    }
}
