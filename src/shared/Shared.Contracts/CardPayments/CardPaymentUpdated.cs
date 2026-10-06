namespace HiWallet.Shared.Contracts.CardPayments;

/// <summary>
/// Kart sağlayıcısı bir ödemenin sonucunu bildirdi. <c>topup-webhook</c> üretir (imzası
/// doğrulanmış webhook'tan), <c>card-topup</c> tüketir.
///
/// Dış gövdenin birebir kopyası DEĞİL: doğrulanmış ve normalize edilmiş hali. Sağlayıcı
/// gövdeyi değiştirdiğinde değişecek yer webhook'un parse'ı.
///
/// Cüzdan ve hesap mesajda YOK: ödemeyi kart yüklemesi servisi açtı ve
/// <see cref="Reference"/> onun kendi kimliği. Para wallet'a ancak kart yüklemesi servisi
/// ödemeyi kendi kaydıyla eşleştirdikten sonra gidiyor.
/// </summary>
public sealed record CardPaymentUpdated
{
    /// <summary>Sağlayıcı kodu (<c>stripe-fake</c>); <c>ledger_accounts.provider</c> ile aynı.</summary>
    public required string Provider { get; init; }

    /// <summary>Sağlayıcının olay kimliği. Inbox'ta <c>(provider, event_id)</c> UNIQUE.</summary>
    public required string EventId { get; init; }

    /// <summary><see cref="CardPaymentEvents"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Sağlayıcının ödeme kimliği.</summary>
    public required string PaymentId { get; init; }

    /// <summary>Ödemeyi açarken verilen kimlik: kartla yüklemenin kimliği.</summary>
    public required Guid Reference { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Olayın sağlayıcı tarafında gerçekleştiği an.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary><see cref="CardPaymentUpdated.Type"/> değerleri.</summary>
public static class CardPaymentEvents
{
    /// <summary>Kart çekildi.</summary>
    public const string Succeeded = "payment.succeeded";

    /// <summary>Müşteri vazgeçti ya da sağlayıcı reddetti; para hareket etmedi.</summary>
    public const string Canceled = "payment.canceled";

    public static bool IsKnown(string? type) => type is Succeeded or Canceled;
}
