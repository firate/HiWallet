namespace HiWallet.WalletService.Infrastructure.Jobs;

public sealed class PromoCampaignOptions
{
    public const string SectionName = "Jobs:PromoCampaign";

    /// <summary>
    /// Ödeme ile promo'nun cüzdana düşmesi arasındaki en uzun süre. Hesap başına günlük
    /// tavan partinin açıldığı gün üzerinden sayıldığı için kısa tutuluyor.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Geriye bakılan süre. Bundan uzun bir kesintide aradaki ödemeler
    /// değerlendirilmiyor (decisions.md madde 37).
    /// </summary>
    public TimeSpan Lookback { get; init; } = TimeSpan.FromDays(1);

    /// <summary>Bir turda değerlendirilecek en fazla ödeme. Kalan sonraki turda.</summary>
    public int BatchSize { get; init; } = 500;
}
