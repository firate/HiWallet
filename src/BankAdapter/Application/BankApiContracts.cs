namespace HiWallet.BankAdapter.Application;

// BANKANIN HTTP SÖZLEŞMESİ, bizim dokümanından yazdığımız hali.
//
// PAYLAŞILAN BİR ASSEMBLY'DEN GELMİYOR (decisions.md madde 35). Bank.Fake kendi
// tiplerini kendi içinde tanımlıyor ve bu dosyanın ondan haberi yok. Ortak tipe
// çıkarılsalardı derleyici iki tarafı senkron tutardı ve "banka sözleşmeyi
// değiştirdi" hatası imkânsız görünürdü — oysa entegrasyonlarda en sık kırılan şey
// tam olarak bu. Gerçeğinde bu dosya bankanın PDF'inden yazılır.
//
// Alan adlarının Bank.Fake'tekilerle aynı olması bir bağ DEĞİL, yalnızca doğru
// yazılmış olmaları.

/// <param name="ClientReference">
/// Bizim referansımız; saga kimliği. ISO 20022'deki <c>EndToEndIdentification</c>
/// karşılığı — banka bunu geri yansıtıyor ve korelasyon bununla kuruluyor.
/// </param>
internal sealed record StartTransferRequest(
    string ClientReference,
    decimal Amount,
    string Currency,
    string DestinationIban);

/// <summary>
/// Bankanın kabul response'u. <c>Status</c> burada her zaman <c>pending</c> —
/// "aldım" demek "gönderdim" demek değil.
/// </summary>
internal sealed record StartTransferResponse(
    string BankReference,
    string Status,
    bool Replayed);

/// <summary>Durum sorgusu response'u. Mutabakat taramasının okuduğu şey.</summary>
internal sealed record TransferStatusResponse(
    string BankReference,
    string ClientReference,
    string Status,
    decimal Amount,
    decimal Fee,
    string Currency,
    string? FailureReason,
    DateTimeOffset AcceptedAt);

/// <summary>
/// Bankanın bildirim tipleri. Banka tek endpoint'e iki tür bildirim gönderiyor ve
/// gövdedeki <c>type</c> hangisi olduğunu söylüyor.
/// </summary>
internal static class BankEventType
{
    /// <summary>
    /// Bizim başlattığımız transferin sonucu. Tip alanı eklenmeden önce gelen bildirimler
    /// de bu sayılıyor: o zaman bankanın tek bildirimi buydu.
    /// </summary>
    public const string TransferStatus = "transfer.status";

    /// <summary>Hesabımıza gelen havale.</summary>
    public const string IncomingTransfer = "transfer.incoming";
}

/// <summary>
/// Bankanın transfer sonucu bildirimi. <c>bank-webhook</c> bunu ham olarak inbox'a
/// yazıyor, relay burada çözüyor.
/// </summary>
internal sealed record CallbackNotification
{
    public required string EventId { get; init; }

    public required string BankReference { get; init; }

    public required string ClientReference { get; init; }

    public required string Status { get; init; }

    public required decimal Fee { get; init; }

    public required string Currency { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// Hesabımıza havale geldi bildirimi. Banka parayı açıklamaya bakmadan kabul etmiş;
/// gönderenin bilgileri bankanın bildirdiği gibi.
/// </summary>
internal sealed record IncomingTransferNotification
{
    public required string EventId { get; init; }

    public required string BankReference { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public string? Description { get; init; }

    public string? SenderName { get; init; }

    public string? SenderIban { get; init; }

    /// <summary>Gönderenin kimlik numarası (TCKN/VKN). Yasa gönderenin bilgilerinin alıcı kuruma taşınmasını istiyor; bunlardan biri.</summary>
    public string? SenderNationalId { get; init; }

    /// <summary>Paranın hesabımıza girdiği an.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    public IncomingTransferItem ToItem() => new(
        BankReference, Amount, Currency, Description, SenderName, SenderIban, SenderNationalId, OccurredAt);
}

/// <summary>Hesap hareketleri: bir zaman aralığında hesabımıza gelen havaleler. Taramanın okuduğu şey.</summary>
internal sealed record IncomingTransfersResponse(IReadOnlyList<IncomingTransferItem> Items);

internal sealed record IncomingTransferItem(
    string BankReference,
    decimal Amount,
    string Currency,
    string? Description,
    string? SenderName,
    string? SenderIban,
    string? SenderNationalId,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Bankanın durum kelimeleri. Bizim <c>BankTransferStatus</c>'umuzla eşlemesi
/// <see cref="BankClient"/>'ta — tek yerde, çünkü sözleşme değişirse orada kırılmalı.
/// </summary>
internal static class BankApiStatus
{
    public const string Pending = "pending";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";
}
