namespace HiWallet.Fakes.Topups;

/// <summary>
/// Sağlayıcının webhook'u NASIL gönderdiği (overview.md madde 9). Sahte servisin
/// tek özel yeteneği bu: dış dünya kötülüklerini bilinçli tetiklemek.
///
/// Gerçek bir sağlayıcıya "şimdi aynı event'i iki kez gönder" diyemezsin; oysa
/// idempotency'nin çalıştığını kanıtlamanın tek yolu bunu tetikleyebilmek.
/// </summary>
public enum TopupDeliveryMode
{
    /// <summary>Tek event, hemen. Mutlu yol.</summary>
    Normal = 1,

    /// <summary>
    /// AYNI event iki kez. <c>event_id</c> de aynı — farklı olsaydı bu iki ayrı
    /// para girişi olurdu, tekrar değil.
    ///
    /// Beklenen: bakiye bir kez artar. İki kademe idempotency devrede — inbox'taki
    /// <c>(provider, event_id)</c> UNIQUE ikinciyi kuyruğa hiç koymaz.
    /// </summary>
    Duplicate = 2,

    /// <summary>
    /// Tek event, gecikmeli. Eventual davranışı görünür kılıyor: response döndüğünde
    /// para henüz cüzdanda değil ve bu bir hata değil.
    /// </summary>
    Delayed = 3,

    /// <summary>
    /// Aynı cüzdana birden fazla event, <b>ters sırada gönderilir</b>: en yeni
    /// <c>occurredAt</c> önce gider.
    ///
    /// <b>Ne kanıtlıyor, ne kanıtlamıyor.</b> Top-up'ta toplama değişmeli olduğu
    /// için sıra nihai bakiyeyi ETKİLEMEZ — bu test "sıra doğru" demiyor.
    /// Dediği şu: aynı cüzdanın N event'inin hepsi iniyor, hiçbiri kaybolmuyor ve
    /// bakiye toplama eşit. Consistent-hash routing hepsini aynı partition'a
    /// düşürdüğü için tek tüketici sırayla işliyor; farklı partition'lara
    /// dağılsalardı aynı cüzdan üzerinde eşzamanlı yazma olur ve optimistic lock
    /// çakışmaları başlardı.
    /// </summary>
    OutOfOrder = 4
}

/// <summary>
/// Gönderilecek top-up request'i. Sağlayıcının kendi iç modeli — bu tip
/// <c>topup-webhook</c>'un gövdesine çevriliyor.
/// </summary>
/// <param name="Count">
/// Yalnızca <see cref="TopupDeliveryMode.OutOfOrder"/>'da anlamlı: kaç event.
/// </param>
public sealed record TopupDelivery(
    Guid WalletId,
    decimal Amount,
    string Currency,
    TopupDeliveryMode Mode,
    int Count = 3,
    int DelayMilliseconds = 500);
