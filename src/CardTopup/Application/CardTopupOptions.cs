namespace HiWallet.CardTopup.Application;

/// <summary>Kartla yüklemenin ayarları. Bölüm eksikse uygulama açılmıyor.</summary>
public sealed class CardTopupOptions
{
    public const string SectionName = "CardTopups";

    /// <summary>
    /// Kart sağlayıcısının bizdeki kodu; wallet'taki <c>ledger_accounts.provider</c> ile AYNI
    /// değer. Pay ve kapanış bu kodla gidiyor, wallet parayı bu sağlayıcının clearing'inden
    /// alıyor.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Sağlayıcının API kökü. Bugün <c>Stripe.Fake</c>'e bakıyor; canlıda sağlayıcının kendi
    /// adresi.
    /// </summary>
    public string? ProviderUrl { get; init; }

    /// <summary>Sağlayıcıya tek çağrının üst sınırı.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Ödeme oturumunun ömrü. Sağlayıcı bu süreden sonra ödeme kabul etmiyor; limit payı ise
    /// oturum kapanana kadar duruyor. Kısa tutuluyor: pay durdukça hesaba başka para giremiyor.
    /// </summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromMinutes(15);

    public CardTopupScanOptions Scan { get; init; } = new();
}

/// <summary>
/// Açık yüklemelerin taraması. <b>KAPATILAMAZ, yalnızca aralığı ayarlanır:</b> oturumun süresi
/// dolduğunda sağlayıcı bildirim göndermiyor; bunu öğrenmenin tek yolu sormak. Tarama koşmasa
/// süresi dolan her yüklemenin payı kalıcı olur ve hesaba bir daha para giremez.
/// </summary>
public sealed class CardTopupScanOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Payı onaylanmamış (<c>created</c>) yüklemenin terk edilmiş sayılması için geçmesi gereken
    /// süre. Wallet'a giden pay isteğinin sürebileceği en uzun süreden ÇOK uzun olmalı.
    /// </summary>
    public TimeSpan CreatedStaleAfter { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Oturumun bitişinden sonra sağlayıcıya sormadan önce beklenen süre: son anda verilen
    /// kararın bildirimi yolda olabilir.
    /// </summary>
    public TimeSpan ExpiryGrace { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Bir turda en fazla kaç yükleme sorulacağı.</summary>
    public int BatchSize { get; init; } = 100;
}
