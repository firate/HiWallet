namespace HiWallet.BankIntegration.Persistence;

/// <summary>
/// Bankadan gelmiş callback. <c>bank-webhook</c> yazıyor, <c>bank-adapter</c>'ın
/// relay'i okuyor.
///
/// <b>Inbox'ın varlık sebebi</b> top-up'takiyle aynı (decisions.md madde 29):
/// bankaya "aldım" demeden önce bildirimin kalıcı olması. Doğrudan işlenip
/// <c>202</c> dönülseydi, veritabanı ya da broker o an erişilemez olduğunda ya
/// bankaya hata dönmek ya da bildirimi kaybetmek kalırdı — ve banka sınırlı sayıda
/// deneyip vazgeçiyor.
///
/// <b>İşlemeyi webhook DEĞİL relay yapıyor</b> ve bu <c>topup-webhook</c>'un
/// şeklinden bilinçli sapma: callback yolu ile mutabakat taraması AYNI kapanış
/// koduna varmak zorunda ve o kodun tek kopyası olmalı. Ayrıca yayın mantığındaki
/// her değişiklik aksi halde bankanın çağırdığı ucu yeniden başlatmayı gerektirirdi
/// (decisions.md madde 35).
/// </summary>
public sealed class BankCallback
{
    public required Guid Id { get; init; }

    /// <summary>
    /// Bildirimi gönderen kurum. Tek bankayla çalışırken sabit ama kolon olarak
    /// duruyor: ikinci bir banka eklendiğinde <c>event_id</c> tekilliği kurum
    /// başına olmak zorunda, yoksa iki bankanın çakışan id'leri birbirini bastırır.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Bankanın event kimliği. <c>(provider, event_id)</c> UNIQUE — bankanın tekrar
    /// gönderdiği aynı bildirim ikinci kez işlenmiyor.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Bankanın gönderdiği HAM gövde. İmza tam olarak bu baytlar üzerinde
    /// doğrulandı; ihtilafta "bize ne geldi" sorusunun tek cevabı bu.
    ///
    /// Normalize edilmiş bir kopya AYRICA tutulmuyor — top-up inbox'ından farklı.
    /// Orada relay broker'a mesaj yayınlıyor ve kalıcı olanla yayınlanan birebir
    /// aynı olmalıydı; burada relay mesaj yayınlamıyor, transferi kapatıyor.
    /// </summary>
    public required string RawPayload { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>İşlendiği an. NULL ise relay'in işi bitmemiş.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    public int ProcessAttempts { get; set; }

    /// <summary>
    /// Son denemenin hatası. Dolu kalan satırlar alarm konusu: bankanın gönderdiği
    /// bir sonucu işleyemiyoruz demek.
    /// </summary>
    public string? LastError { get; set; }
}
