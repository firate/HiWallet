namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// Ledger transaction tipi. Beş transfer tipi aynı çekirdekten geçer, tip yalnızca
/// policy katmanını değiştirir (overview.md madde 4).
/// </summary>
public enum LedgerTransactionType
{
    /// <summary>person → person</summary>
    P2P = 1,

    /// <summary>person → business</summary>
    P2B = 2,

    /// <summary>business → person</summary>
    B2P = 3,

    /// <summary>business → business</summary>
    B2B = 4,

    /// <summary>Ödeme. Komisyon kesilen tip.</summary>
    Payment = 5,

    /// <summary>Dışarıdan para girişi. Cüzdan +, clearing −.</summary>
    Topup = 6,

    /// <summary>Dışarıya para çıkışı. Cüzdan −, clearing +.</summary>
    Withdrawal = 7,

    /// <summary>Compensation. Ters kayıt — silme değil (overview.md madde 6).</summary>
    Refund = 8,

    /// <summary>Sağlayıcı settlement'ı. Clearing kapanır, nostro hareket eder.</summary>
    Settlement = 9,

    /// <summary>Invoiced modelde fatura kaydı. Idempotency key = fatura numarası (decisions.md madde 11).</summary>
    ProviderInvoice = 10,

    /// <summary>Promo yükleme. Müşterinin promo kovası +, fonlayan − (decisions.md madde 37).</summary>
    PromoGrant = 11,

    /// <summary>Süresi dolan promo partisinin kalanının kapatılması (decisions.md madde 37).</summary>
    PromoExpiry = 12
}

/// <summary>
/// <see cref="LedgerTransactionType"/>'ın metin karşılıkları. Kolon değeri ve API
/// gövdesi aynı kaynaktan besleniyor; iki yerde ayrı yazılsalardı biri
/// değiştiğinde diğeri sessizce eski değerle kalırdı (FundType ile aynı gerekçe).
/// </summary>
public static class LedgerTransactionTypes
{
    public static string ToText(this LedgerTransactionType type)
    {
        return type switch
        {
            LedgerTransactionType.P2P => "p2p",
            LedgerTransactionType.P2B => "p2b",
            LedgerTransactionType.B2P => "b2p",
            LedgerTransactionType.B2B => "b2b",
            LedgerTransactionType.Payment => "payment",
            LedgerTransactionType.Topup => "topup",
            LedgerTransactionType.Withdrawal => "withdrawal",
            LedgerTransactionType.Refund => "refund",
            LedgerTransactionType.Settlement => "settlement",
            LedgerTransactionType.ProviderInvoice => "provider_invoice",
            LedgerTransactionType.PromoGrant => "promo_grant",
            LedgerTransactionType.PromoExpiry => "promo_expiry",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Eşlemesi yazılmamış işlem tipi.")
        };
    }

    public static LedgerTransactionType FromText(string text)
    {
        return text switch
        {
            "p2p" => LedgerTransactionType.P2P,
            "p2b" => LedgerTransactionType.P2B,
            "b2p" => LedgerTransactionType.B2P,
            "b2b" => LedgerTransactionType.B2B,
            "payment" => LedgerTransactionType.Payment,
            "topup" => LedgerTransactionType.Topup,
            "withdrawal" => LedgerTransactionType.Withdrawal,
            "refund" => LedgerTransactionType.Refund,
            "settlement" => LedgerTransactionType.Settlement,
            "provider_invoice" => LedgerTransactionType.ProviderInvoice,
            "promo_grant" => LedgerTransactionType.PromoGrant,
            "promo_expiry" => LedgerTransactionType.PromoExpiry,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen işlem tipi.")
        };
    }
}

