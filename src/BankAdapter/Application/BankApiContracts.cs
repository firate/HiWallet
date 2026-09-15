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
/// Bankanın kabul yanıtı. <c>Status</c> burada her zaman <c>pending</c> —
/// "aldım" demek "gönderdim" demek değil.
/// </summary>
internal sealed record StartTransferResponse(
    string BankReference,
    string Status,
    bool Replayed);

/// <summary>Durum sorgusu yanıtı. Mutabakat taramasının okuduğu şey.</summary>
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
/// Bankanın callback gövdesi. <c>bank-webhook</c> bunu ham olarak inbox'a yazıyor,
/// relay burada çözüyor.
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
/// Bankanın durum kelimeleri. Bizim <c>BankTransferStatus</c>'umuzla eşlemesi
/// <see cref="BankClient"/>'ta — tek yerde, çünkü sözleşme değişirse orada kırılmalı.
/// </summary>
internal static class BankApiStatus
{
    public const string Pending = "pending";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";
}
