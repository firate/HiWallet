namespace HiWallet.Bank.Fake.Infrastructure.Persistence;

/// <summary>
/// Bir çekim için önceden kurulmuş senaryo. Kurulmamışsa varsayılan davranış
/// uygulanıyor (<c>BankFakeOptions.DefaultOutcome</c>, normalde başarı).
///
/// <b>Bu tablo bankanın İÇ bilgisi.</b> <c>bank-adapter</c> ona erişemiyor — ayrı
/// veritabanı, ayrı kullanıcı (decisions.md madde 35). Paylaşılsaydı adaptör
/// "senaryo ne diyormuş" diye bakabilirdi ve simülasyon o an değerini kaybederdi:
/// gerçek bankanın kararını önceden okuyamazsın.
///
/// <b>Neden veritabanında.</b> Bellekte tutulsaydı servis yeniden başlayınca
/// kaybolurdu ve iki instance farklı davranırdı. Özellikle
/// <see cref="RemainingTransientFailures"/> kalıcı olmak zorunda: "üç denemede
/// başarılı" davranışı restart'ta başa dönerdi.
///
/// <b>Neden çekim (client reference) başına.</b> IBAN'a bağlansaydı aynı hesaba
/// yapılan iki farklı çekim birbirinin senaryosunu bozardı; paralel koşan testler
/// için kullanılamazdı.
/// </summary>
internal sealed class TransferScenario
{
    /// <summary>
    /// Bizim saga kimliğimiz — bankaya <c>clientReference</c> olarak gidiyor.
    /// Testler çekimi başlatmadan önce bu değeri biliyor.
    /// </summary>
    public required string ClientReference { get; init; }

    public required string Outcome { get; init; }

    /// <summary>
    /// Geçici hata kaç kez üretilecek. Her denemede azalıyor; sıfıra indiğinde
    /// transfer kabul ediliyor. "Transient sonra başarılı" senaryosu bununla kuruluyor.
    /// </summary>
    public int RemainingTransientFailures { get; set; }

    /// <summary>
    /// <see cref="TransferOutcome.DelayedSuccess"/>'te sonucun belli olması için
    /// geçecek süre. Testin toplam süresini belirlediği için üst sınırı var.
    /// </summary>
    public required int DelayMilliseconds { get; init; }

    /// <summary>
    /// Bu çekim için bankaya kaç istek ulaştı — geçici hatayla sonuçlananlar dahil.
    /// <see cref="RemainingTransientFailures"/>'tan TÜRETİLEMEZ: o sayaç sıfıra
    /// inince kaç denemede indiği bilgisi kayboluyor.
    /// </summary>
    public int Attempts { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
