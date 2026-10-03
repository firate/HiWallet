using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;

namespace HiWallet.UnitTests.Promos;

/// <summary>
/// Kampanyanın ödülü ve tanım kuralları (decisions.md madde 37).
/// </summary>
public sealed class PromoCampaignTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid Merchant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static PromoCampaign Campaign(
        PromoCampaignRule rule = PromoCampaignRule.PaymentToMerchant,
        decimal? threshold = null,
        PromoRewardType rewardType = PromoRewardType.Fixed,
        decimal? amount = 10m,
        decimal? rate = null,
        decimal? max = null,
        PromoScope scope = PromoScope.AllBusinesses,
        Guid[]? triggers = null,
        Guid[]? scopeMerchants = null) =>
        PromoCampaign.Create(
            Guid.NewGuid(), "Test", rule, threshold, rewardType, amount, rate, max,
            Currency.From("TRY"), scope, grantValidFor: null,
            budget: 1_000m, dailyCapPerAccount: 100m, totalCapPerAccount: 500m,
            startsAt: T0, endsAt: null,
            triggers ?? (rule is PromoCampaignRule.PaymentToMerchant ? [Merchant] : []),
            scopeMerchants ?? [], T0);

    [Fact]
    public void SabitOdul_TabandanBagimsiz()
    {
        Campaign(amount: 15m).RewardFor(1_000m).ShouldBe(15m);
    }

    [Fact]
    public void YuzdeOdul_KucukBirimeAsagiYuvarlanir()
    {
        var campaign = Campaign(rewardType: PromoRewardType.Percentage, amount: null, rate: 0.05m, max: 100m);

        campaign.RewardFor(33.33m).ShouldBe(1.66m, "1.6665 aşağı yuvarlanıyor");
    }

    [Fact]
    public void YuzdeOdul_TavanaKirpilir()
    {
        var campaign = Campaign(rewardType: PromoRewardType.Percentage, amount: null, rate: 0.10m, max: 20m);

        campaign.RewardFor(1_000m).ShouldBe(20m);
    }

    [Fact]
    public void GunlukEsikte_YuzdeOdulReddedilir()
    {
        Should.Throw<ArgumentException>(() => Campaign(
            rule: PromoCampaignRule.DailyPaymentTotal, threshold: 1_000m,
            rewardType: PromoRewardType.Percentage, amount: null, rate: 0.05m, max: 50m));
    }

    [Fact]
    public void GunlukEsikte_EsikZorunlu()
    {
        Should.Throw<ArgumentException>(() => Campaign(rule: PromoCampaignRule.DailyPaymentTotal, threshold: null));
    }

    [Fact]
    public void IsyerineOdemede_TetikleyenIsyeriZorunlu()
    {
        Should.Throw<ArgumentException>(() => Campaign(triggers: []));
    }

    [Fact]
    public void SeciliKapsamda_IsyeriListesiZorunlu()
    {
        Should.Throw<ArgumentException>(() => Campaign(scope: PromoScope.SelectedBusinesses, scopeMerchants: []));
    }

    [Fact]
    public void YuzdeOdulde_TavanZorunlu()
    {
        Should.Throw<ArgumentException>(() => Campaign(
            rewardType: PromoRewardType.Percentage, amount: null, rate: 0.05m, max: null));
    }

    [Fact]
    public void Bitirme_SimdiBitiyorVeBitireniYaziyor()
    {
        var campaign = Campaign();

        campaign.End("calisan-1", T0.AddDays(3));

        campaign.EndsAt.ShouldBe(T0.AddDays(3));
        campaign.EndedBy.ShouldBe("calisan-1");
        campaign.IsActiveAt(T0.AddDays(3)).ShouldBeFalse();
        campaign.IsActiveAt(T0.AddDays(2)).ShouldBeTrue();
    }

    /// <summary>Başlamamış kampanya başlangıcında bitiyor: hiç çalışmıyor.</summary>
    [Fact]
    public void BaslamadanBitirme_HicCalismaz()
    {
        var campaign = Campaign();

        campaign.End("calisan-1", T0.AddDays(-1));

        campaign.EndsAt.ShouldBe(T0);
        campaign.IsActiveAt(T0).ShouldBeFalse();
    }

    /// <summary>Bitmiş kampanya yeniden bitirilince bitişi ve bitireni değişmiyor.</summary>
    [Fact]
    public void BitmisKampanya_Degismez()
    {
        var campaign = Campaign();
        campaign.End("calisan-1", T0.AddDays(3));

        campaign.End("calisan-2", T0.AddDays(5));

        campaign.EndsAt.ShouldBe(T0.AddDays(3));
        campaign.EndedBy.ShouldBe("calisan-1");
    }
}
