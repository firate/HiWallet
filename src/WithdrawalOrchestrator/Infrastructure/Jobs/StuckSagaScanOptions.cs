namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Jobs;

public sealed class StuckSagaScanOptions
{
    public const string SectionName = "Jobs:StuckSagaScan";

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Bir saga'nın "takılmış" sayılması için son geçişinden bu yana geçmesi gereken
    /// süre.
    ///
    /// Normal bir çekim saniyeler içinde bitiyor, yani eşiğin mutlu yolu yakalama
    /// riski yok. Yine de cömert tutuluyor: broker'ın kısa süreli kesintisi ya da
    /// bankanın yavaşlaması takılma DEĞİL, ve her böyle olayda alarm üretmek alarmı
    /// değersizleştirir — kimse bakmaz hale gelir.
    /// </summary>
    public TimeSpan Threshold { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Alarmda kaç saga kimliği yazılacak. Sınırsız olsaydı yaygın bir kesintide
    /// tek log satırı binlerce kimlik taşırdı; sayı zaten ayrı raporlanıyor.
    /// </summary>
    public int SampleSize { get; init; } = 20;
}
