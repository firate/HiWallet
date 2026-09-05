namespace HiWallet.BankService.Application;

/// <summary>
/// Sahte bankanın üretebileceği sonuçlar (overview.md madde 9).
///
/// Dördü de gerçek bir bankada karşılığı olan durumlar; buradaki fark yalnızca
/// hangisinin olacağına testin karar verebilmesi.
/// </summary>
public enum TransferOutcome
{
    /// <summary>Transfer gerçekleşti. Varsayılan.</summary>
    Success = 1,

    /// <summary>
    /// Geçici hata: ağ kesintisi, banka tarafında zaman aşımı. Saga'ya
    /// BİLDİRİLMİYOR — bank-service kendi içinde yeniden deniyor. Her başarısız
    /// denemede telafi başlatmak müşterinin parasını gereksiz yere ileri geri
    /// taşırdı (overview.md madde 6).
    /// </summary>
    TransientFailure = 2,

    /// <summary>
    /// Kalıcı hata: hesap kapalı, IBAN sahibi uyuşmuyor. Yeniden denemek aynı
    /// sonucu verir; saga'ya bildiriliyor ve telafi başlıyor.
    /// </summary>
    PermanentFailure = 3,

    /// <summary>
    /// Gecikmeli başarı. Sonuç <see cref="Success"/> ile aynı; amaç eventual
    /// davranışı görünür kılmak — saga'nın "bekliyor" durumunda gerçekten
    /// beklediğini gösteriyor.
    /// </summary>
    DelayedSuccess = 4
}
