using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Promos;

/// <summary>
/// Promo kampanyası (decisions.md madde 37): hangi ödeme promo kazandırıyor, ne kadar,
/// verilen parti nerede ve ne kadar süre geçerli, ve üç sınır. Kampanyadan gelen parti
/// platform fonlu.
///
/// Backoffice gelene kadar SQL ile yönetiliyor; fabrika testler ve ileride backoffice
/// için, kuralları DB'deki CHECK'lerle aynı.
///
/// Şema: docs/ledger-schema.md "promo_campaigns".
/// </summary>
public sealed class PromoCampaign
{
    private readonly List<PromoCampaignMerchant> _merchants = [];

    private PromoCampaign()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public PromoCampaignRule Rule { get; private set; }

    /// <summary><see cref="PromoCampaignRule.DailyPaymentTotal"/>'da zorunlu, diğerinde NULL.</summary>
    public decimal? ThresholdAmount { get; private set; }

    public PromoRewardType RewardType { get; private set; }

    /// <summary><see cref="PromoRewardType.Fixed"/>'da zorunlu.</summary>
    public decimal? RewardAmount { get; private set; }

    /// <summary><see cref="PromoRewardType.Percentage"/>'da zorunlu; 0.05 = %5.</summary>
    public decimal? RewardRate { get; private set; }

    /// <summary><see cref="PromoRewardType.Percentage"/>'da zorunlu tavan.</summary>
    public decimal? RewardMax { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>Verilen partinin kapsamı. Yükleme anında partiye kopyalanıyor.</summary>
    public PromoScope GrantScope { get; private set; }

    /// <summary>Verilen partinin geçerlilik süresi. NULL ise parti süresiz.</summary>
    public TimeSpan? GrantValidFor { get; private set; }

    public decimal Budget { get; private set; }

    public decimal DailyCapPerAccount { get; private set; }

    public decimal TotalCapPerAccount { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>NULL ise açık uçlu.</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<PromoCampaignMerchant> Merchants => _merchants;

    /// <summary>Tetikleyen işyerleri: <see cref="PromoCampaignRule.PaymentToMerchant"/>'ta.</summary>
    public IEnumerable<Guid> TriggerMerchants =>
        _merchants.Where(m => m.Role is PromoCampaignMerchantRole.Trigger).Select(m => m.AccountId);

    /// <summary>Verilen partinin geçerli olduğu işyerleri: <see cref="PromoScope.SelectedBusinesses"/>'ta.</summary>
    public IEnumerable<Guid> ScopeMerchants =>
        _merchants.Where(m => m.Role is PromoCampaignMerchantRole.Scope).Select(m => m.AccountId);

    /// <summary>
    /// Ödeme tabanına göre ödül, sınırlardan önce. Taban müşterinin card ve cash ile
    /// ödediği tutar; promo payı ve komisyon dahil değil. Yüzde ödül küçük birime aşağı
    /// yuvarlanıyor.
    /// </summary>
    public decimal RewardFor(decimal paidWithOwnFunds)
    {
        return RewardType switch
        {
            PromoRewardType.Fixed => RewardAmount!.Value,
            PromoRewardType.Percentage => Math.Min(
                decimal.Round(paidWithOwnFunds * RewardRate!.Value, Currency.MinorUnit, MidpointRounding.ToZero),
                RewardMax!.Value),
            _ => throw new InvalidOperationException($"Bilinmeyen ödül tipi: {RewardType}.")
        };
    }

    public bool IsActiveAt(DateTimeOffset at) => StartsAt <= at && (EndsAt is null || at < EndsAt);

    public static PromoCampaign Create(
        Guid id,
        string name,
        PromoCampaignRule rule,
        decimal? thresholdAmount,
        PromoRewardType rewardType,
        decimal? rewardAmount,
        decimal? rewardRate,
        decimal? rewardMax,
        Currency currency,
        PromoScope grantScope,
        TimeSpan? grantValidFor,
        decimal budget,
        decimal dailyCapPerAccount,
        decimal totalCapPerAccount,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        IEnumerable<Guid> triggerMerchants,
        IEnumerable<Guid> scopeMerchants,
        DateTimeOffset createdAt)
    {
        var triggers = triggerMerchants.Distinct().ToArray();
        var scope = scopeMerchants.Distinct().ToArray();

        Require(!string.IsNullOrWhiteSpace(name), "Kampanyanın adı olmalı.");
        Require((rule is PromoCampaignRule.DailyPaymentTotal) == (thresholdAmount is not null),
            "Eşik yalnızca günlük ödeme toplamı kuralında ve orada zorunlu.");
        Require(thresholdAmount is null or > 0m, "Eşik pozitif olmalı.");
        Require(rule is PromoCampaignRule.PaymentToMerchant || rewardType is PromoRewardType.Fixed,
            "Yüzde ödül yalnızca işyerine ödeme kuralında.");
        Require((rule is PromoCampaignRule.PaymentToMerchant) == (triggers.Length > 0),
            "Tetikleyen işyerleri yalnızca işyerine ödeme kuralında ve orada zorunlu.");
        Require((rewardType is PromoRewardType.Fixed) == (rewardAmount is not null),
            "Sabit tutar yalnızca sabit ödülde ve orada zorunlu.");
        Require((rewardType is PromoRewardType.Percentage) == (rewardRate is not null && rewardMax is not null),
            "Yüzde ödülde oran ve tavan zorunlu, sabit ödülde ikisi de boş.");
        Require(rewardAmount is null or > 0m, "Ödül tutarı pozitif olmalı.");
        Require(rewardRate is null or > 0m and <= 1m, "Oran 0 ile 1 arasında olmalı.");
        Require(rewardMax is null or > 0m, "Ödül tavanı pozitif olmalı.");
        Require((grantScope is PromoScope.SelectedBusinesses) == (scope.Length > 0),
            "Seçili işyerleri kapsamında işyeri listesi zorunlu, her yerde geçerli kapsamda boş.");
        Require(grantValidFor is null || grantValidFor > TimeSpan.Zero, "Geçerlilik süresi pozitif olmalı.");
        Require(budget > 0m && dailyCapPerAccount > 0m && totalCapPerAccount > 0m,
            "Bütçe ve iki hesap tavanı zorunlu ve pozitif.");
        Require(endsAt is null || endsAt > startsAt, "Bitiş başlangıçtan sonra olmalı.");

        var campaign = new PromoCampaign
        {
            Id = id,
            Name = name,
            Rule = rule,
            ThresholdAmount = thresholdAmount,
            RewardType = rewardType,
            RewardAmount = rewardAmount,
            RewardRate = rewardRate,
            RewardMax = rewardMax,
            Currency = currency,
            GrantScope = grantScope,
            GrantValidFor = grantValidFor,
            Budget = budget,
            DailyCapPerAccount = dailyCapPerAccount,
            TotalCapPerAccount = totalCapPerAccount,
            StartsAt = startsAt,
            EndsAt = endsAt,
            CreatedAt = createdAt
        };

        campaign._merchants.AddRange(
            triggers.Select(a => new PromoCampaignMerchant(id, a, PromoCampaignMerchantRole.Trigger)));
        campaign._merchants.AddRange(
            scope.Select(a => new PromoCampaignMerchant(id, a, PromoCampaignMerchantRole.Scope)));

        return campaign;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}

/// <summary>Kampanyaya bağlı işyeri hesabı: tetikleyen ya da verilen partinin kapsamı.</summary>
public sealed class PromoCampaignMerchant
{
    private PromoCampaignMerchant()
    {
        // EF Core materialization.
    }

    internal PromoCampaignMerchant(Guid campaignId, Guid accountId, PromoCampaignMerchantRole role)
    {
        CampaignId = campaignId;
        AccountId = accountId;
        Role = role;
    }

    public Guid CampaignId { get; private set; }

    public Guid AccountId { get; private set; }

    public PromoCampaignMerchantRole Role { get; private set; }
}

public enum PromoCampaignRule
{
    /// <summary>Kampanyanın işyerlerinden birine yapılan ödeme.</summary>
    PaymentToMerchant = 1,

    /// <summary>Hesabın günlük (UTC) ödeme toplamını eşiğe ulaştıran ödeme.</summary>
    DailyPaymentTotal = 2
}

public enum PromoRewardType
{
    Fixed = 1,
    Percentage = 2
}

public enum PromoCampaignMerchantRole
{
    Trigger = 1,
    Scope = 2
}
