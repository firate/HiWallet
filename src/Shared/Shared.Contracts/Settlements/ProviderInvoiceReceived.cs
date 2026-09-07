namespace HiWallet.Shared.Contracts.Settlements;

/// <summary>
/// Sağlayıcının dönem sonu faturası. topup-webhook üretir, wallet-service tüketir.
///
/// Yalnızca <c>Invoiced</c> modeldeki sağlayıcılar için anlamlı: <c>Net</c> modelde
/// ücret settlement anında kesiliyor ve fatura diye bir adım yok
/// (<c>decisions.md</c> madde 10).
///
/// Ledger etkisi, ancak fatura beklenen toplamla tutarsa
/// (<c>ledger-schema.md</c> "Invoiced model"):
/// <code>
/// provider_expense  -4200.00
/// nostro            +4200.00   banka hesabından çıkan gerçek tutar
/// </code>
/// </summary>
public sealed record ProviderInvoiceReceived
{
    public required string Provider { get; init; }

    /// <summary>
    /// Fatura numarası. Idempotency buna dayanıyor — aynı faturayı iki kez işlemek
    /// doğrudan yanlış gider kaydı üretir (madde 11).
    /// </summary>
    public required string InvoiceRef { get; init; }

    public required string Currency { get; init; }

    /// <summary>Faturadaki toplam tutar.</summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Faturanın kapsadığı sağlayıcı işlem referansları
    /// (<c>provider_fees.provider_ref</c>).
    ///
    /// Boş bırakılabilir: bazı sağlayıcılar yalnızca toplam gönderiyor. O durumda
    /// kapsam "bu sağlayıcının faturalanmamış tüm ücretleri" olarak alınıyor ve
    /// madde 11'deki kalem bazlı fark analizi yapılamıyor — yalnızca toplam
    /// karşılaştırılıyor.
    /// </summary>
    public required IReadOnlyList<string> ProviderRefs { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }
}
