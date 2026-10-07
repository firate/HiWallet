namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// Bir işletmenin bir günlük özeti (<c>overview.md</c> madde 7).
///
/// <b>Ledger DEĞİL, türetilmiş rapor.</b> Buradaki her satır
/// <c>ledger_entries</c>'ten yeniden hesaplanabiliyor; tersi geçerli değil.
/// Bozulursa silinip yeniden üretilir, düzeltme kaydı gerekmez — append-only
/// kuralı bu tabloya işlemiyor.
///
/// <b>Neyi sayıyor (varsayım — docs'ta tanım yok).</b> Komisyonu GÖNDEREN ödüyor
/// (<c>overview.md</c> madde 4: gönderen −102, alan +100, revenue +2), yani
/// işletmenin "kestiği" bir komisyon yok. Rapor işletmenin TAHSİLATINI ve o
/// tahsilatın platforma kazandırdığı geliri gösteriyor:
///
/// <list type="bullet">
/// <item><c>Volume</c> — işletmenin cüzdanlarına GİREN toplam.</item>
/// <item><c>TransactionCount</c> — bu girişleri yapan işlem sayısı.</item>
/// <item><c>Commission</c> — aynı işlemlerin <c>revenue</c> bacaklarının toplamı;
/// müşteri ödedi, işletmenin cirosu üretti.</item>
/// </list>
///
/// Çekim ve para gönderme sayılmıyor: rapor "bu işletme ne kadar sattı" sorusunu
/// cevaplıyor, "kasası ne kadar hareket etti" sorusunu değil.
/// </summary>
internal sealed class BusinessDailySummary
{
    public Guid AccountId { get; init; }

    /// <summary>UTC gün. Yerel saat dilimi YOK — ledger'daki her şey UTC.</summary>
    public DateOnly Day { get; init; }

    public string Currency { get; init; } = string.Empty;

    public int TransactionCount { get; set; }

    public decimal Volume { get; set; }

    public decimal Commission { get; set; }

    /// <summary>
    /// Satırın en son ne zaman hesaplandığı. Job kapalı günleri yeniden hesaplayıp
    /// üzerine yazıyor; bu alan "rapor ne kadar taze" sorusunu cevaplıyor.
    /// </summary>
    public DateTimeOffset CalculatedAt { get; set; }
}
