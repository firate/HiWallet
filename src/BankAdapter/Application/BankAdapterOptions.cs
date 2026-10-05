namespace HiWallet.BankAdapter.Application;

/// <summary>
/// Banka entegrasyonunun ayarları (decisions.md madde 35).
/// </summary>
public sealed class BankAdapterOptions
{
    public const string SectionName = "Bank";

    /// <summary>
    /// Bankanın API kökü. Bugün <c>Bank.Fake</c>'e bakıyor; canlıda bankanın
    /// kendi adresi. Adaptör kodunda hiçbir değişiklik gerekmiyor — sahtelik
    /// tamamen bu değerde.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>Tek bir çağrının üst sınırı. Aşıldığında GEÇİCİ hata sayılıyor.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public ReconciliationOptions Reconciliation { get; init; } = new();

    /// <summary>
    /// Bankanın bizdeki kodu; wallet'taki <c>ledger_accounts.provider</c> ile AYNI değer.
    /// Hesap hareketi taramasının bulduğu havale bu kodla yazılıyor. Bildirimle gelen
    /// havalede kod inbox satırından, yani bankanın çağırdığı adresten geliyor.
    /// </summary>
    public string? Provider { get; init; }

    public DepositReconciliationOptions DepositReconciliation { get; init; } = new();
}

/// <summary>
/// Hesap hareketi taraması: bildirimi kaçırılmış havaleleri bankanın hesap hareketlerinden
/// bulup kaydeder.
///
/// <b>KAPATILAMAZ, yalnızca aralığı ayarlanır</b> (decisions.md madde 35). Kapatılabilseydi
/// kaçırılan bir bildirim müşterinin parasını bankada bırakırdı: para hesabımızda ama ne
/// cüzdanda ne askıda, ledger bankadan ayrışmış.
/// </summary>
public sealed class DepositReconciliationOptions
{
    /// <summary>Tarama sıklığı. Bildirim asıl yol olduğu için SEYREK.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Bir havalenin "bildirimi kaçırıldı" sayılması için geçmesi gereken süre. Daha
    /// yeni havaleler taranmıyor: bildirimleri yolda olabilir. Callback çalışırken tarama
    /// boş dönüyor; bulduğu her havale alarm sinyali.
    /// </summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Ne kadar geriye bakıldığı. Aralıktan uzun olmalı: taramanın koşmadığı bir
    /// dönem (servis kapalıydı) bir sonraki turda yine kapsansın.
    /// </summary>
    public TimeSpan Lookback { get; init; } = TimeSpan.FromDays(3);
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
