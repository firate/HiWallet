namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// Ledger transaction tipi. Beş transfer tipi aynı çekirdekten geçer, tip yalnızca
/// policy katmanını değiştirir (Bölüm 3 madde 4).
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

    /// <summary>Compensation. Ters kayıt — silme değil (Bölüm 3 madde 6).</summary>
    Refund = 8,

    /// <summary>Sağlayıcı settlement'ı. Clearing kapanır, nostro hareket eder.</summary>
    Settlement = 9,

    /// <summary>Invoiced modelde fatura kaydı. Idempotency key = fatura numarası (decisions.md madde 11).</summary>
    ProviderInvoice = 10
}
