namespace HiWallet.BankAdapter.Application;

/// <summary>
/// Banka entegrasyonunun ayarları (decisions.md madde 35).
/// </summary>
public sealed class BankAdapterOptions
{
    public const string SectionName = "Bank";

    /// <summary>
    /// Bankanın API kökü. Bugün <c>Bank.Fake</c>'e bakıyor; üretimde bankanın
    /// kendi adresi. Adaptör kodunda hiçbir değişiklik gerekmiyor — sahtelik
    /// tamamen bu değerde.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>Tek bir çağrının üst sınırı. Aşıldığında GEÇİCİ hata sayılıyor.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public ReconciliationOptions Reconciliation { get; init; } = new();
}

/// <summary>
/// Mutabakat taraması: callback'i kaçırılmış transferleri bankaya sorup kapatır.
///
/// <b>KAPATILAMAZ, yalnızca aralığı ayarlanır</b> (decisions.md madde 35).
/// Kapatılabilseydi kaçırılan bir callback kalıcı kayıp olurdu: satır <c>pending</c>
/// kalır, saga <c>bank_transfer_pending</c>'de asılır ve müşterinin parası
/// clearing'de durur.
/// </summary>
public sealed class ReconciliationOptions
{
    /// <summary>
    /// Tarama sıklığı. Callback asıl yol olduğu için bu SEYREK — günde birkaç kez.
    /// Callback'i olmayan bir bankayla çalışılıyorsa buranın kısalması gerekiyor,
    /// çünkü o kurulumda tarama asıl yol oluyor.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Bir transferin "cevapsız kaldı" sayılması için geçmesi gereken süre.
    ///
    /// Taramanın kapsamını daraltan şey bu: her bekleyen transfer değil, yalnızca
    /// bu süreden uzundur bekleyenler soruluyor. Callback çalışırken tarama
    /// neredeyse boş dönüyor — dolu dönmeye başlaması doğrudan alarm sinyali.
    /// </summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Bir turda en fazla kaç transfer sorulacağı. Banka hız sınırına saygı.</summary>
    public int BatchSize { get; init; } = 100;
}
