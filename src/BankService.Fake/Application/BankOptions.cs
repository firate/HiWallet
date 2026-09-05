namespace HiWallet.BankService.Application;

/// <summary>
/// Senaryosu kurulmamış transferlerin varsayılan davranışı.
///
/// <b>Neden var.</b> Senaryo saga başına kuruluyor ama saga kimliğini ancak çekim
/// isteği kabul edildikten SONRA öğrenebiliyorsun. Araya banka komutunun sığması
/// teorik olarak mümkün ve o zaman senaryo geç kalırdı. Varsayılanı önceden
/// ayarlayabilmek bu yarışı tamamen kaldırıyor: "bu ayaktaki banka her şeyi
/// reddeder" denip servis öyle başlatılıyor.
///
/// Demo ortamında da işe yarıyor: telafi yolunu göstermek için tek bir ayar yetiyor.
/// </summary>
public sealed class BankOptions
{
    public const string SectionName = "Bank";

    public TransferOutcome DefaultOutcome { get; init; } = TransferOutcome.Success;

    /// <summary>
    /// <see cref="TransferOutcome.TransientFailure"/> varsayılanında kaç kez geçici
    /// hata üretilecek. Saga başına sayılıyor.
    /// </summary>
    public int DefaultTransientFailures { get; init; } = 1;
}
