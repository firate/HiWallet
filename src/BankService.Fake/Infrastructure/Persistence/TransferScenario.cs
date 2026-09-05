namespace HiWallet.BankService.Infrastructure.Persistence;

/// <summary>
/// Bir saga için önceden kurulmuş senaryo. Kurulmamışsa varsayılan davranış
/// (<see cref="Application.TransferOutcome.Success"/>) uygulanıyor.
///
/// <b>Neden veritabanında.</b> Bellekte tutulsaydı senaryo servis yeniden başlayınca
/// kaybolurdu ve iki instance farklı davranırdı — sahte de olsa bu servisin
/// öngörülebilir olması gerekiyor, testler ona bakıyor.
///
/// <b>Neden saga başına.</b> IBAN'a bağlansaydı aynı hesaba yapılan iki farklı çekim
/// birbirinin senaryosunu bozardı; paralel koşan testler için kullanılamazdı.
/// </summary>
internal sealed class TransferScenario
{
    public required Guid SagaId { get; init; }

    public required string Outcome { get; init; }

    /// <summary>
    /// Geçici hata kaç kez üretilecek. Her denemede azalıyor; sıfıra indiğinde
    /// transfer başarıyla sonuçlanıyor. "Transient sonra başarılı" senaryosu
    /// (overview.md madde 9) bununla kuruluyor.
    /// </summary>
    public int RemainingTransientFailures { get; set; }

    /// <summary>
    /// <see cref="Application.TransferOutcome.DelayedSuccess"/>'te beklenecek süre.
    /// Testin toplam süresini belirlediği için üst sınırı var.
    /// </summary>
    public required int DelayMilliseconds { get; init; }

    /// <summary>
    /// Bu saga için handler'a kaç komut ulaştı — geçici hatayla sonuçlananlar dahil.
    /// <see cref="RemainingTransientFailures"/>'tan TÜRETİLEMEZ: o sayaç sıfıra
    /// inince kaç denemede indiği bilgisi kayboluyor.
    /// </summary>
    public int Attempts { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
