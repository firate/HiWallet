namespace HiWallet.WithdrawalOrchestrator.Domain;

/// <summary>
/// Saga durumları (overview.md madde 6). Sıra anlamlı değil, isimler anlamlı;
/// yine de sayılar sabit — durum veritabanında metin olarak duruyor ama enum
/// değerinin kayması log ve metrik geçmişini bozar.
/// </summary>
public enum WithdrawalState
{
    /// <summary>İstek alındı, henüz para hareketi yok.</summary>
    Initiated = 1,

    /// <summary>Kural/limit reddi. HİÇBİR para hareketi olmadı — terminal.</summary>
    Rejected = 2,

    /// <summary>Cüzdandan düşüldü, para "yolda" (clearing'de).</summary>
    Debited = 3,

    /// <summary>Bankaya komut gitti, cevap bekleniyor.</summary>
    BankTransferPending = 4,

    /// <summary>Banka transferi tamamlandı — terminal.</summary>
    Completed = 5,

    /// <summary>Banka kalıcı olarak başarısız; ters kayıt yazılıyor.</summary>
    Compensating = 6,

    /// <summary>Telafi tamamlandı, müşterinin parası geri verildi — terminal.</summary>
    Failed = 7
}

/// <summary>
/// Bir event'in saga üzerindeki etkisi.
///
/// Üç sonuç var çünkü iki farklı "beklenmeyen" durum var ve bunları aynı kefeye
/// koymak tehlikeli. <c>overview.md</c> madde 6 "tekrar gelen event mevcut durumla
/// eşleşmezse yok sayılır" diyor; kod bunun ilerisine geçiyor (bkz. decisions.md
/// madde 31).
/// </summary>
public enum TransitionResult
{
    /// <summary>Geçiş yapıldı.</summary>
    Applied = 1,

    /// <summary>
    /// Zararsız tekrar: aynı event ikinci kez geldi ya da saga çoktan ilerlemiş.
    /// Broker en az bir kez teslim ettiği için BEKLENEN bir durum. Ack'lenir.
    /// </summary>
    Ignored = 2,

    /// <summary>
    /// Çelişki: event mevcut durumla bağdaşmıyor ve bu para kaybına işaret
    /// edebiliyor (telafiden sonra gelen "başarılı" gibi). Yok sayılmaz —
    /// alarm üretilir, saga olduğu yerde bırakılır.
    /// </summary>
    Conflict = 3
}
