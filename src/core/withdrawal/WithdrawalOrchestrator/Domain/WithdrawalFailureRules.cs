namespace HiWallet.WithdrawalOrchestrator.Domain;

/// <summary>
/// Saga'nın kendi koyduğu sebep adları. Reddetmede ad wallet'tan geliyor
/// (<c>WithdrawalDebitRejected.Rule</c>); bu ikisi para düştükten sonraki kapanışlar.
/// </summary>
public static class WithdrawalFailureRules
{
    /// <summary>Banka transferi kabul etmedi; para cüzdana geri döndü.</summary>
    public const string BankRejected = "bank_rejected";

    /// <summary>Çalışan incelemede iptal etti; para cüzdana geri döndü.</summary>
    public const string ReviewCancelled = "review_cancelled";
}
