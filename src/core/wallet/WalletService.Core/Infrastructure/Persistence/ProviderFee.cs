using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// Sağlayıcı ücretinin beklenen/gerçekleşen takibi ve fatura eşleştirmesi
/// (<c>ledger-schema.md</c> "provider_fees", <c>decisions.md</c> madde 10).
///
/// <b>Ledger DEĞİL.</b> Zero-sum invariant'ına dahil değil, hiçbir bakiyeyi
/// etkilemiyor. <see cref="ExpectedAmount"/> ledger'a ASLA yazılmaz: gerçekleşmiş bir
/// hareket değil, bir tahmin. Ledger'ın "burada yazan her şey gerçekleşmiştir"
/// özelliği bu ayrımla korunuyor.
///
/// <b>Ayrı tablo olmasının ikinci sebebi yaşam döngüsü.</b> Fatura eşleştirmesi
/// sonradan UPDATE istiyor (<see cref="InvoiceRef"/>, <see cref="ActualAmount"/>);
/// ledger ise immutable. Ücret kolonları <c>ledger_transactions</c>'a eklenseydi
/// hem append-only kuralı delinir hem de kolonlar transferlerde (hacmin çoğunluğu)
/// hep NULL kalırdı.
/// </summary>
internal sealed class ProviderFee
{
    public Guid Id { get; init; }

    /// <summary>Ücretin doğduğu ledger işlemi (top-up, çekim).</summary>
    public Guid TransactionId { get; init; }

    /// <summary><c>ledger_accounts.provider</c> ile aynı değer.</summary>
    public string Provider { get; init; } = string.Empty;

    public FeeSettlement SettlementModel { get; init; }

    /// <summary>
    /// Bugün tek değer: <c>provider</c>. Kolon yine de var çünkü sağlayıcılar
    /// birden fazla kalem kesiyor (işlem ücreti, chargeback, kur farkı) ve bunları
    /// ayırmadan fatura uyuşmazlığı analizi yapılamaz (madde 11).
    /// </summary>
    public string FeeType { get; init; } = "provider";

    /// <summary>Tarifeden hesaplanan beklenti. Ledger'a yazılmaz.</summary>
    public decimal ExpectedAmount { get; init; }

    /// <summary>
    /// Gerçekleşen. Yalnızca <c>Net</c> modelde, settlement anında dolar; sıfır
    /// YAZILMAZ — "ücret alınmadı" ile "henüz bilmiyoruz" ayrımı kaybolurdu.
    ///
    /// <b><c>Invoiced</c> modelde HİÇ DOLMUYOR</b> ve NULL kalması kusur değil:
    /// fatura toplam tutarı bildiriyor, onu satır başına dağıtmak uydurma bir
    /// hassasiyet olurdu (settlement tarafındaki aynı gerekçe). O modelde satırın
    /// kapandığını gösteren şey <see cref="InvoiceRef"/> — faturanın kapsam sorgusu
    /// da onu kullanıyor, buradaki değeri değil.
    /// </summary>
    public decimal? ActualAmount { get; set; }

    public string Currency { get; init; } = string.Empty;

    /// <summary>Sağlayıcının işlem referansı; fatura kalemleriyle eşleştirmenin anahtarı.</summary>
    public string? ProviderRef { get; init; }

    /// <summary>Fatura numarası. <c>Net</c> modelde hiç dolmaz.</summary>
    public string? InvoiceRef { get; set; }

    /// <summary>Gider kaydının ledger işlemi. Yazılana kadar NULL.</summary>
    public Guid? LedgerTransactionId { get; set; }

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Uyuşmazlık kabul edildiyse gerekçe (madde 11).</summary>
    public string? Note { get; set; }
}
