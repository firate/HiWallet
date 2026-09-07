namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// Sağlayıcının dönem sonu faturası (<c>decisions.md</c> madde 11).
///
/// <b>Neden ayrı tablo.</b> <c>ledger-schema.md</c> "fatura özeti ayrı tablo
/// gerektirmez, <c>invoice_ref</c> ile grupla" diyor ve UYGULANMIŞ fatura için bu
/// doğru — satırlar <c>provider_fees</c>'te duruyor. Ama <c>PendingReview</c>'da
/// bekleyen faturanın hiçbir satırı yok: ledger'a yazılmadı, ücret satırları
/// işaretlenmedi. Gruplanacak bir şey olmadığı için kaydı tutacak yer de yok.
/// Madde 11 "kayıt PendingReview durumunda bekler" diyor; beklediği yer burası.
///
/// Ledger DEĞİL: <see cref="Amount"/> faturanın iddiası, gerçekleşen hareket değil.
/// Gerçekleşen hareket ancak fatura uygulandığında yazılıyor ve o kaydın kimliği
/// <see cref="LedgerTransactionId"/>'de duruyor.
/// </summary>
internal sealed class ProviderInvoice
{
    public Guid Id { get; init; }

    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// Sağlayıcının fatura numarası. <c>(provider, invoice_ref)</c> UNIQUE —
    /// aynı faturayı iki kez işlemek doğrudan yanlış gider kaydı demek ve manuel
    /// tetiklenen akışlarda gerçek bir risk (madde 11).
    /// </summary>
    public string InvoiceRef { get; init; } = string.Empty;

    /// <summary>Faturadaki toplam tutar. Sağlayıcının iddiası.</summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Bizim beklediğimiz toplam: kapsanan <c>provider_fees.expected_amount</c>
    /// toplamı. Karşılaştırmanın diğer tarafı; kayıtta duruyor ki inceleme sırasında
    /// yeniden hesaplanması gerekmesin.
    /// </summary>
    public decimal ExpectedAmount { get; init; }

    public string Currency { get; init; } = string.Empty;

    public ProviderInvoiceStatus Status { get; set; }

    /// <summary>Faturanın kapsadığı ücret satırı sayısı.</summary>
    public int FeeCount { get; init; }

    /// <summary>Gider kaydının ledger işlemi. <c>PendingReview</c>'da NULL.</summary>
    public Guid? LedgerTransactionId { get; set; }

    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>
    /// Tolerans içinde kalan fark ya da inceleme sonucu düşülen gerekçe.
    /// Madde 11'in üç çıkış durumu da manuel ve izi buraya düşüyor.
    /// </summary>
    public string? Note { get; set; }
}

public enum ProviderInvoiceStatus
{
    /// <summary>Ledger'a yazıldı, ücret satırları kapandı.</summary>
    Applied = 1,

    /// <summary>
    /// Fatura ile beklenen toplam tolerans dışında ayrıştı. Ledger'a HİÇBİR ŞEY
    /// yazılmadı — sistem hangi tarafın haklı olduğuna karar veremez (madde 11).
    /// </summary>
    PendingReview = 2
}

public static class ProviderInvoiceStatuses
{
    /// <summary>
    /// Kolona yazılan değer. <c>ToString()</c> yetmez: enum yeniden adlandırıldığında
    /// şema sessizce ayrışırdı.
    /// </summary>
    public static string ToText(this ProviderInvoiceStatus status) => status switch
    {
        ProviderInvoiceStatus.Applied => "applied",
        ProviderInvoiceStatus.PendingReview => "pending_review",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Eşlemesi yazılmamış durum.")
    };

    public static ProviderInvoiceStatus FromText(string text) => text switch
    {
        "applied" => ProviderInvoiceStatus.Applied,
        "pending_review" => ProviderInvoiceStatus.PendingReview,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen fatura durumu.")
    };
}
