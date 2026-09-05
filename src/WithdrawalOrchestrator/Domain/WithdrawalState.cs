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

public static class WithdrawalStates
{
    /// <summary>
    /// Saga'nın işi bitti mi. Tek yerde duruyor çünkü iki ayrı tüketicisi var:
    /// <see cref="WithdrawalSaga.IsTerminal"/> ve takılmış saga taramasının
    /// kullandığı kısmi index'in filtresi. İkisi ayrı listeler tutsaydı yeni bir
    /// terminal durum eklendiğinde tarama bitmiş saga'ları "takılmış" diye
    /// raporlamaya başlardı.
    /// </summary>
    public static bool IsTerminal(this WithdrawalState state) =>
        state is WithdrawalState.Rejected or WithdrawalState.Completed or WithdrawalState.Failed;

    /// <summary>Devam eden durumlar. Kısmi index filtresi bundan üretiliyor.</summary>
    public static IEnumerable<WithdrawalState> Active =>
        Enum.GetValues<WithdrawalState>().Where(state => !state.IsTerminal());

    /// <summary>
    /// Durumun dış dünyadaki adı. TEK yerde duruyor çünkü üç tüketicisi var:
    /// veritabanı kolonu, HTTP yanıtı ve index filtresi. Üçü ayrı yazılsaydı
    /// biri değiştiğinde diğerleri sessizce eskir; kolon ile filtre ayrıştığında
    /// hiçbir derleme hatası çıkmaz.
    ///
    /// <c>ToString()</c> yeterli değil: PascalCase isim yeniden adlandırmayla
    /// değişir ve o anda hem şema hem sözleşme kırılır.
    /// </summary>
    public static string ToText(this WithdrawalState state)
    {
        return state switch
        {
            WithdrawalState.Initiated => "initiated",
            WithdrawalState.Rejected => "rejected",
            WithdrawalState.Debited => "debited",
            WithdrawalState.BankTransferPending => "bank_transfer_pending",
            WithdrawalState.Completed => "completed",
            WithdrawalState.Compensating => "compensating",
            WithdrawalState.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Eşlemesi yazılmamış saga durumu.")
        };
    }

    public static WithdrawalState FromText(string text)
    {
        return text switch
        {
            "initiated" => WithdrawalState.Initiated,
            "rejected" => WithdrawalState.Rejected,
            "debited" => WithdrawalState.Debited,
            "bank_transfer_pending" => WithdrawalState.BankTransferPending,
            "completed" => WithdrawalState.Completed,
            "compensating" => WithdrawalState.Compensating,
            "failed" => WithdrawalState.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen saga durumu.")
        };
    }
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
