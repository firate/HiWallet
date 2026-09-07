namespace HiWallet.WalletService.Infrastructure.Jobs;

public sealed class BusinessSummaryOptions
{
    public const string SectionName = "Jobs:BusinessSummary";

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Her turda kaç kapalı gün yeniden hesaplanacak.
    ///
    /// Bir günden fazlası bilerek: servis birkaç saat kapalı kalırsa ya da bir tur
    /// hata alırsa eksik gün kendiliğinden dolsun. Yeniden hesaplamak güvenli —
    /// kapalı bir günün ledger'ı değişmiyor ve upsert aynı satırı üzerine yazıyor.
    ///
    /// Uzun bir kesintiyi kapatmıyor: 2 günden eski boşluklar için değer geçici
    /// olarak yükseltilir. Otomatik geri doldurma yazılmadı — ilk turda tüm
    /// geçmişi taramak, kesinti olmayan her dağıtımda boşa iş demek olurdu.
    /// </summary>
    public int LookbackDays { get; init; } = 2;
}
