namespace HiWallet.Bank.Fake.Infrastructure.Storage;

/// <summary>
/// Bankanın, hesabımıza gelen havale kaydı: hesap hareketi. Banka parayı açıklamaya
/// bakmadan kabul ediyor; kime ait olduğuna karar vermek bizim işimiz.
///
/// Gönderenin bilgileri gönderen bankanın mesajla taşıdığı haliyle: adı, IBAN'ı ve kimlik
/// numarası. Yasa bu bilgilerin alıcı kuruma kadar taşınmasını istiyor.
/// </summary>
internal sealed class IncomingTransfer
{
    /// <summary>Bankanın ürettiği referans. Hem bildirimde hem hesap hareketlerinde aynı.</summary>
    public required string BankReference { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public string? Description { get; init; }

    public string? SenderName { get; init; }

    public string? SenderIban { get; init; }

    public string? SenderNationalId { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>
    /// Bu havale için bildirim gönderilecek mi. <c>false</c> kaçırılmış bildirimi taklit
    /// ediyor: havale yalnızca hesap hareketlerinde görünüyor.
    /// </summary>
    public required bool Notify { get; init; }

    /// <summary>Bildirimin başarıyla gönderildiği an. NULL ise henüz başaramadı ya da gönderilmeyecek.</summary>
    public DateTimeOffset? CallbackSentAt { get; set; }

    public int CallbackAttempts { get; set; }

    public string? LastCallbackError { get; set; }
}
