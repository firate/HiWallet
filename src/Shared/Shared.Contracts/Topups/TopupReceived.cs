namespace HiWallet.Shared.Contracts.Topups;

/// <summary>
/// Sağlayıcının "para yatırıldı" bildiriminin iç karşılığı. topup-webhook üretir,
/// wallet-service tüketir.
///
/// Dış payload'ın birebir kopyası DEĞİL: sağlayıcı ne gönderirse göndersin buraya
/// doğrulanmış ve normalize edilmiş hali giriyor. Sağlayıcı payload'ı değiştirdiğinde
/// değişecek yer webhook'un parse'ı, tüketici tarafı değil.
///
/// Alanlar <c>init</c>; yeni alan eklemek eski üreticileri bozmuyor. Positional record
/// olsaydı constructor imzası değişir, sözleşme kırılırdı.
/// </summary>
public sealed record TopupReceived
{
    /// <summary>Sağlayıcı kodu (<c>stripe-fake</c>, <c>bank-fake</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Sağlayıcının event id'si. Idempotency'nin dayandığı değer — hem inbox'ta
    /// <c>(provider, event_id)</c> UNIQUE, hem tüketicide <c>processed_events</c>.
    /// Sağlayıcı bazında tekil kabul ediliyor, global değil; bu yüzden her yerde
    /// <c>provider</c> ile birlikte taşınıyor.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>Paranın gireceği cüzdan. Partition anahtarı da bu (overview.md madde 8).</summary>
    public required Guid LedgerAccountId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    /// Sağlayıcının kendi ödeme referansı. Ledger'da kullanılmıyor; mutabakatta
    /// fatura kalemleriyle eşleştirmek için taşınıyor (decisions.md madde 11).
    /// </summary>
    public required string ProviderRef { get; init; }

    /// <summary>Olayın sağlayıcı tarafında gerçekleştiği an. Bizim aldığımız an değil.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
