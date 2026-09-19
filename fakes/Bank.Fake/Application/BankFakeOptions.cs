namespace HiWallet.Bank.Fake.Application;

/// <summary>
/// Sahte bankanın davranışı. Gerçek bir bankada bunların hiçbiri konfigürasyon
/// değil — banka nasıl davranacağına kendi karar verir.
/// </summary>
public sealed class BankFakeOptions
{
    public const string SectionName = "BankFake";

    /// <summary>
    /// Senaryosu kurulmamış transferlerin davranışı.
    ///
    /// <b>Neden var.</b> Senaryo çekim başına kuruluyor ama saga kimliğini ancak
    /// çekim request'i kabul edildikten SONRA öğrenebiliyorsun. Araya banka çağrısının
    /// sığması mümkün ve o zaman senaryo geç kalırdı. Varsayılanı önceden
    /// ayarlayabilmek bu yarışı tamamen kaldırıyor: "bu ayaktaki banka her şeyi
    /// reddeder" denip servis öyle başlatılıyor.
    /// </summary>
    public TransferOutcome DefaultOutcome { get; init; } = TransferOutcome.Success;

    /// <summary>
    /// <see cref="TransferOutcome.TransientFailure"/> varsayılanında kaç kez geçici
    /// hata üretilecek.
    /// </summary>
    public int DefaultTransientFailures { get; init; } = 1;

    /// <summary>
    /// Bankanın transfer başına kestiği ücret. Gerçek entegrasyonda bankanın
    /// tarifesinden gelir; burada sabit.
    ///
    /// Sıfır olabilir ama varsayılanı sıfır DEĞİL: ücretsiz bir banka, wallet
    /// tarafındaki gider bacağını hiç sınamayan bir kurulum demek olurdu.
    /// </summary>
    public decimal TransferFee { get; init; } = 1.50m;

    /// <summary>
    /// Kabul edilen bir transferin sonucunun belli olması için geçen süre. Senaryo
    /// kendi gecikmesini vermediğinde bu kullanılıyor.
    ///
    /// Sıfır DEĞİL: sıfır olsaydı sonuç kabul anında hazır olur ve asenkron yapının
    /// tek gözlemlenebilir tarafı — <c>bank_transfer_pending</c> penceresi —
    /// testlerde hiç görünmezdi.
    /// </summary>
    public TimeSpan SettlementDelay { get; init; } = TimeSpan.FromSeconds(2);

    public CallbackOptions Callback { get; init; } = new();
}

/// <summary>
/// Bankanın bizi geri araması. Kapalıyken sahte banka hiçbir çağrı yapmıyor ve
/// sonucu yalnızca <c>GET /transfers/{ref}</c> ile veriyor — durum sorgusu var ama
/// callback'i olmayan bankaları temsil ediyor.
/// </summary>
public sealed class CallbackOptions
{
    public bool Enabled { get; init; }

    /// <summary>Adaptörün callback endpoint'i. Örnek: <c>http://bank-webhook:8080/v1/webhooks/bank/bank-fake</c>.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// Gövdeyi imzalayan paylaşılan secret. Bizim tarafımızdaki doğrulayıcıyla
    /// AYNI olmak zorunda; ayrışırsa her callback <c>401</c> alır ve sonuç yalnızca
    /// mutabakat taramasıyla öğrenilir.
    /// </summary>
    public string? Secret { get; init; }

    /// <summary>Gönderilecek callback var mı diye ne sıklıkla bakılacağı.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Kaç kez denendikten sonra vazgeçileceği. Gerçek bankalar da sonsuza kadar
    /// denemiyor — vazgeçtiği nokta, bizim tarafta mutabakat taramasının neden
    /// zorunlu olduğunun kanıtı (decisions.md madde 35).
    /// </summary>
    public int MaxAttempts { get; init; } = 5;
}
