namespace HiWallet.CardTopup.Application;

/// <summary>
/// Ödenmeden kapanan yüklemenin sebepleri. Arayüz mesajı bunlardan seçiyor; wallet'ın reddinde
/// sebep wallet'ın kural adı (<c>card_topup_limit</c>, ...).
/// </summary>
public static class FailureReasons
{
    /// <summary>Müşteri ödeme sayfasında vazgeçti ya da sağlayıcı kartı reddetti.</summary>
    public const string Canceled = "canceled";

    /// <summary>Ödeme oturumu müşteri ödemeden kapandı.</summary>
    public const string Expired = "expired";

    /// <summary>Pay isteğinin cevabı hiç alınamadı ve yükleme terk edildi.</summary>
    public const string Abandoned = "abandoned";

    /// <summary>Sağlayıcıda ödeme hiç açılmadı ve oturumun süresi doldu.</summary>
    public const string PaymentNotOpened = "payment_not_opened";

    /// <summary>Sağlayıcı ödeme açma isteğimizi reddetti.</summary>
    public const string ProviderRejected = "provider_rejected";
}
