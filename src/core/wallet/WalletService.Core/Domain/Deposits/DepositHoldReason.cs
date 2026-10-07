namespace HiWallet.WalletService.Domain.Deposits;

/// <summary>Havalenin cüzdana geçirilemeyip askıya alınma sebebi.</summary>
public enum DepositHoldReason
{
    /// <summary>Açıklamada geçerli bir hesap numarası yok.</summary>
    NoAccountNumber = 1,

    /// <summary>Açıklamada birden fazla farklı geçerli numara var; hangisi olduğu tahmin edilmiyor.</summary>
    AmbiguousAccountNumber = 2,

    /// <summary>Numara geçerli ama o numarada hesap yok.</summary>
    UnknownAccount = 3,

    /// <summary>İşyeri hesabı: havaleyle yükleme bireysel hesapta.</summary>
    BusinessAccount = 4,

    /// <summary>Hesabın bu para biriminde cüzdanı yok; kendiliğinden cüzdan açılmıyor.</summary>
    NoWalletInCurrency = 5,

    /// <summary>
    /// Bankanın bildiriminde gönderenin kimlik numarası yok: gönderenin hesabın sahibi
    /// olduğu doğrulanamıyor.
    /// </summary>
    UnknownSender = 6,

    /// <summary>
    /// Gönderen hesabın sahibi değil. Yalnızca müşterinin kendi adına kayıtlı banka
    /// hesabından gelen havale kabul ediliyor.
    /// </summary>
    SenderNotHolder = 7,

    /// <summary>Seviyenin aylık limiti ya da bakiye tavanı.</summary>
    LimitExceeded = 8
}

/// <summary>
/// <see cref="DepositHoldReason"/>'ın metin karşılıkları. Kolon değeri ve API gövdesi
/// aynı kaynaktan besleniyor (<c>LedgerTransactionTypes</c> ile aynı gerekçe).
/// </summary>
public static class DepositHoldReasons
{
    public static string ToText(this DepositHoldReason reason)
    {
        return reason switch
        {
            DepositHoldReason.NoAccountNumber => "no_account_number",
            DepositHoldReason.AmbiguousAccountNumber => "ambiguous_account_number",
            DepositHoldReason.UnknownAccount => "unknown_account",
            DepositHoldReason.BusinessAccount => "business_account",
            DepositHoldReason.NoWalletInCurrency => "no_wallet_in_currency",
            DepositHoldReason.UnknownSender => "unknown_sender",
            DepositHoldReason.SenderNotHolder => "sender_not_holder",
            DepositHoldReason.LimitExceeded => "limit_exceeded",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Eşlemesi yazılmamış askı sebebi.")
        };
    }

    public static DepositHoldReason FromText(string text)
    {
        return text switch
        {
            "no_account_number" => DepositHoldReason.NoAccountNumber,
            "ambiguous_account_number" => DepositHoldReason.AmbiguousAccountNumber,
            "unknown_account" => DepositHoldReason.UnknownAccount,
            "business_account" => DepositHoldReason.BusinessAccount,
            "no_wallet_in_currency" => DepositHoldReason.NoWalletInCurrency,
            "unknown_sender" => DepositHoldReason.UnknownSender,
            "sender_not_holder" => DepositHoldReason.SenderNotHolder,
            "limit_exceeded" => DepositHoldReason.LimitExceeded,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen askı sebebi.")
        };
    }
}
