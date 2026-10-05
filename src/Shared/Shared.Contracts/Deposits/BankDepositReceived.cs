namespace HiWallet.Shared.Contracts.Deposits;

/// <summary>
/// Banka hesabımıza havale geldi. <c>bank-adapter</c> üretir (bankanın bildiriminden ya da
/// hesap hareketi taramasından), <c>wallet-consumer</c> tüketir.
///
/// Para bu mesaj yayınlandığında ZATEN bankamızda: banka parayı açıklamaya bakmadan kabul
/// ediyor. Wallet'ın işi parayı reddetmek değil, nereye yazılacağına karar vermek:
/// açıklamadaki hesap numarasının varsayılan cüzdanı ya da askı.
///
/// Gönderenin adı ve IBAN'ı mesajda YOK: wallet'ın kararı için gerekmiyor ve kişisel veri
/// banka entegrasyonunun veritabanında kalıyor. Kimlik numarası yalnızca gönderenin hesabın
/// sahibi olduğunu doğrulamak için taşınıyor; wallet onu yazmıyor.
/// </summary>
public sealed record BankDepositReceived
{
    /// <summary>Parayı alan banka; <c>ledger_accounts.provider</c> ile aynı değer.</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Bankanın gelen işlem referansı. Idempotency buna dayanıyor: aynı havale hem
    /// bildirimle hem taramayla gelebilir ve broker aynı mesajı iki kez teslim edebilir.
    /// </summary>
    public required string BankReference { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Gönderenin yazdığı açıklama, olduğu gibi. Hesap numarası buradan okunuyor.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gönderenin kimlik numarası (TCKN), bankanın bildirdiği. Yoksa gönderenin hesabın
    /// sahibi olduğu doğrulanamıyor ve para askıya düşüyor.
    /// </summary>
    public string? SenderNationalId { get; init; }

    /// <summary>Paranın bankaya girdiği an, bankanın bildirdiği.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}
