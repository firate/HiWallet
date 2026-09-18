using HiWallet.Bank.Fake.Application;

namespace HiWallet.Bank.Fake.Infrastructure.Storage;

/// <summary>
/// Bankanın kendi transfer kaydı. Adaptördeki <c>bank_transfers</c> ile KARIŞTIRILMAMALI:
/// o bizim "ne gönderdik" kaydımız, bu bankanın "ne aldık" kaydı. İkisi ayrı
/// yerlerde duruyor ve ikisi de kendi tarafının doğruluk kaynağı.
///
/// Gerçek bir entegrasyonda bu kaydın karşılığı bankanın sisteminde duruyor ve biz
/// onu hiç göremiyoruz — yalnızca <c>bank_reference</c> ile soruyoruz.
/// </summary>
internal sealed class BankTransfer
{
    /// <summary>
    /// Bankanın ürettiği referans. Dışarıya verdiği tek kimlik bu; bizim komut
    /// kimliğimiz değil.
    /// </summary>
    public required string BankReference { get; init; }

    /// <summary>
    /// Müşterinin (bizim) kendi referansı — ISO 20022'deki <c>EndToEndIdentification</c>
    /// karşılığı. Saga kimliğini taşıyor.
    ///
    /// Senaryolar bunun üzerinden kuruluyor: test çekimi başlatmadan ÖNCE saga
    /// kimliğini biliyor, ama bizim komut kimliğimizi (outbox satır id'si) bilmiyor.
    /// IBAN'a bağlansaydı aynı hesaba yapılan iki çekim birbirinin senaryosunu bozardı.
    /// </summary>
    public required string ClientReference { get; init; }

    /// <summary>
    /// Müşterinin gönderdiği idempotency anahtarı (bizde <c>CommandId</c>). Tekil —
    /// aynı anahtarla ikinci request YENİ transfer açmıyor, mevcut kaydı dönüyor.
    ///
    /// Bankanın kendi koruması bu; bizim tarafımızdaki dedup'tan bağımsız. İki
    /// taraf da kendi kaydını tutuyor, çünkü gerçek entegrasyonda karşı tarafın
    /// koruma yaptığına güvenilmez.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string DestinationIban { get; init; }

    /// <summary>
    /// Senaryodan çözülen sonuç. Kayıt açılırken SABİTLENİYOR, sorgu anında yeniden
    /// hesaplanmıyor: senaryo sonradan değiştirilse bile başlamış bir transferin
    /// sonucu değişmemeli. Gerçek bankada da öyle olurdu.
    /// </summary>
    public required TransferOutcome Outcome { get; init; }

    /// <summary>
    /// Sonucun hangi andan itibaren belli olduğu. <see cref="TransferOutcome.DelayedSuccess"/>
    /// dışında kabul anı + <c>SettlementDelay</c>.
    ///
    /// Gecikmeyi <c>Task.Delay</c> ile beklemek yerine zaman damgası olarak tutmak
    /// bekleyen request'in bir thread tutmamasını ve durum sorgusu ile callback'in
    /// aynı cevabı vermesini sağlıyor.
    /// </summary>
    public required DateTimeOffset ResolveAt { get; init; }

    /// <summary>Bankanın kestiği ücret. Başarısız transferde de kesiliyor olabilir.</summary>
    public required decimal Fee { get; init; }

    public required DateTimeOffset AcceptedAt { get; init; }

    /// <summary>
    /// Callback'in başarıyla gönderildiği an. NULL ise gönderici henüz başaramadı.
    /// Callback kapalıysa da NULL kalıyor — o kurulumda sonucu adaptör sorarak
    /// öğreniyor.
    /// </summary>
    public DateTimeOffset? CallbackSentAt { get; set; }

    /// <summary>
    /// Callback kaç kez denendi. Gerçek bankalar da sınırlı sayıda dener ve
    /// vazgeçer; bu sayaç o davranışı görünür kılıyor.
    /// </summary>
    public int CallbackAttempts { get; set; }

    public string? LastCallbackError { get; set; }
}
