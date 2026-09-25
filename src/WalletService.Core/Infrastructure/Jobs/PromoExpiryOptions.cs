namespace HiWallet.WalletService.Infrastructure.Jobs;

public sealed class PromoExpiryOptions
{
    public const string SectionName = "Jobs:PromoExpiry";

    /// <summary>
    /// Süresi dolan parti bu aralık kadar cüzdanın promo bakiyesinde görünmeye devam
    /// ediyor; ödemeye ise bitiş anından itibaren girmiyor (decisions.md madde 37).
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Bir turda kapatılacak en fazla parti. Kalan sonraki turda.</summary>
    public int BatchSize { get; init; } = 500;
}
