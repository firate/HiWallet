namespace HiWallet.BankService.Infrastructure.Persistence;

/// <summary>
/// İşlenmiş bir transfer komutu ve ona verilen cevap.
///
/// <b>Aynı zamanda <c>processed_messages</c>.</b> Anahtarı <c>CommandId</c> ve
/// decisions.md madde 32'nin "komutu tüketen tarafta deduplikasyon" gereğini bu
/// tablo karşılıyor. Ayrı bir deduplikasyon tablosu açılmadı çünkü aynı bilgiyi iki
/// yerde tutmak olurdu: "bu komut işlendi mi" sorusunun cevabı zaten "bu transfer
/// kaydı var mı".
///
/// Wallet tarafındaki <c>processed_messages</c> gibi burası da <b>cevabı saklıyor</b>.
/// Sıra: kaydı yaz → cevabı yayınla → ack. Yayın başarısızsa mesaj yeniden teslim
/// ediliyor ve o teslimde transfer İKİNCİ KEZ yapılmadan saklanan cevap yeniden
/// gönderiliyor. Saklanmasaydı ikinci teslim sessizce ack'lenir ve saga sonsuza kadar
/// beklerdi.
/// </summary>
internal sealed class BankTransfer
{
    /// <summary>Komutun <c>CommandId</c>'si; bankanın idempotency anahtarı.</summary>
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    /// Hedef IBAN. Maskesiz saklanıyor: gerçek bir banka da gönderdiği hesabı
    /// biliyor ve mutabakat bunun üzerinden yapılıyor. Log'a maskeli düşüyor.
    /// </summary>
    public required string DestinationIban { get; init; }

    public required string Outcome { get; init; }

    /// <summary>Bankanın kendi referansı. Başarısız transferde NULL.</summary>
    public string? BankReference { get; init; }

    public string? FailureReason { get; init; }

    /// <summary>
    /// Kaçıncı denemede sonuçlandı. Geçici hata senaryosunda 1'den büyük;
    /// retry'ın gerçekten çalıştığının kanıtı bu kolon.
    /// </summary>
    public required int Attempts { get; init; }

    public required DateTimeOffset ProcessedAt { get; init; }

    /// <summary>Yayınlanan cevabın routing key'i, yani event tipinin adı.</summary>
    public required string ReplyRoutingKey { get; init; }

    /// <summary>
    /// Yayınlanan cevabın JSON hali. Kolon <c>jsonb</c> — teşhis sorgulanabilir olsun
    /// diye. Postgres anahtar sırasını normalize ettiği için geri okunan gövde ilk
    /// yayınlananla anlamca aynı, karakter karakter değil.
    /// </summary>
    public required string ReplyPayload { get; init; }
}
