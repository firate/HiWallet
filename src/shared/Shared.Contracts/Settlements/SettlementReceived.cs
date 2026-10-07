namespace HiWallet.Shared.Contracts.Settlements;

/// <summary>
/// Sağlayıcının "şu batch'i ödedim" bildiriminin iç karşılığı. topup-webhook üretir,
/// wallet-service tüketir.
///
/// <b>Top-up settlement'ı.</b> Çekim tarafının settlement'ı burada YOK: orada ücreti
/// bildiren taraf banka ve bilgi saga üzerinden dönüyor, ayrı bir akış
/// (<c>decisions.md</c> madde 13, adım 5.5b).
///
/// Ledger etkisi (<c>ledger-schema.md</c> "Settlement kayıtları"):
/// <code>
/// clearing         +100    alacak kapanır
/// provider_expense  -2.9   yalnızca Net modelde
/// nostro           -97.1   banka hesabına giren gerçek tutar
/// </code>
/// </summary>
public sealed record SettlementReceived
{
    /// <summary>Sağlayıcı kodu; <c>ledger_accounts.provider</c> ile aynı değer.</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Sağlayıcının batch referansı. Idempotency buna dayanıyor: inbox'ta
    /// <c>(provider, event_id)</c>, tüketicide <c>processed_events</c> ve ledger'da
    /// <c>idempotency_key</c>. Aynı batch'i iki kez işlemek clearing'i yanlış
    /// kapatır ve nostro'yu şişirir.
    /// </summary>
    public required string SettlementId { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    /// Kapanan alacağın toplamı — clearing'e yazılacak tutar. Sağlayıcının kestiği
    /// ücret DAHİL, yani bizim ona fatura ettiğimiz brüt.
    /// </summary>
    public required decimal GrossAmount { get; init; }

    /// <summary>
    /// Sağlayıcının kestiği ücret. <c>Net</c> modelde pozitif,
    /// <c>Invoiced</c> modelde sıfır — o modelde ücret settlement'ta değil dönem
    /// sonu faturasında ödeniyor ve settlement kaydında
    /// <c>provider_expense</c> bacağı olmuyor (<c>decisions.md</c> madde 10).
    /// </summary>
    public required decimal FeeAmount { get; init; }

    /// <summary>
    /// Batch'in kapsadığı sağlayıcı işlem referansları
    /// (<c>provider_fees.provider_ref</c>). Ücret satırlarının
    /// <c>actual_amount</c>'ını eşleştirmenin anahtarı bu.
    /// </summary>
    public required IReadOnlyList<string> ProviderRefs { get; init; }

    /// <summary>Sağlayıcı tarafındaki settlement anı. Bizim aldığımız an değil.</summary>
    public required DateTimeOffset SettledAt { get; init; }

    /// <summary>
    /// Banka hesabımıza gerçekten giren tutar. Sağlayıcının bildirdiği değer
    /// TÜRETİLMİYOR: <c>Gross - Fee</c> hesaplanabilirdi ama sağlayıcı yuvarlamayı
    /// kendi yapıyor ve aradaki kuruş farkı nostro ile banka ekstresi arasında
    /// kalıcı bir sapma bırakırdı.
    /// </summary>
    public required decimal NetAmount { get; init; }
}
