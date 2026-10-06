namespace HiWallet.Shared.Contracts.CardTopups;

/// <summary>
/// Kartla yükleme kapandı. <c>card-topup</c> üretir (sağlayıcının bildiriminden ya da
/// kendi taramasının sağlayıcıya sorduğu sonuçtan), <c>wallet-consumer</c> tüketir.
///
/// <c>paid</c>'de para kart sağlayıcısında, cüzdana yazılıyor. <c>failed</c>'de hiçbir para
/// hareket etmedi; ödeme başlarken ayrılan limit payı serbest kalıyor.
///
/// Cüzdan ve hesap mesajda YOK: ödeme başlarken wallet payı kendisi yazdı ve onları biliyor.
/// Mesaj yalnızca hangi payın nasıl kapandığını söylüyor; wallet tutarı kendi kaydıyla
/// karşılaştırıyor.
/// </summary>
public sealed record CardTopupClosed
{
    /// <summary>Kart yüklemesinin kimliği; wallet'taki payın kimliği de bu.</summary>
    public required Guid CardTopupId { get; init; }

    /// <summary><c>paid</c> ya da <c>failed</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary>Kart sağlayıcısı; <c>ledger_accounts.provider</c> ile aynı değer.</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Sağlayıcının ödeme kimliği. Ledger'da kullanılmıyor; settlement ve faturayla
    /// eşleştirmek için sağlayıcı ücretinin kaydına yazılıyor.
    /// </summary>
    public required string ProviderRef { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Sonucun sağlayıcı tarafında kesinleştiği an.</summary>
    public required DateTimeOffset ClosedAt { get; init; }
}

/// <summary><see cref="CardTopupClosed.Outcome"/> değerleri.</summary>
public static class CardTopupClosedOutcomes
{
    public const string Paid = "paid";

    public const string Failed = "failed";
}
