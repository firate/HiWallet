namespace HiWallet.WalletService.Domain.CardTopups;

/// <summary>
/// Kartla yüklemenin kapanışı: pay artık limitten ayrılmış sayılmıyor. Ödendiyse para
/// aynı transaction'da cüzdana yazıldı ve ledger işlemi burada; ödenmediyse pay serbest
/// kaldı. Pay başına tek satır ve satır değişmiyor: kapanmış bir ödeme yeniden açılmıyor.
/// </summary>
public sealed class CardTopupHoldClosure
{
    private CardTopupHoldClosure()
    {
    }

    public Guid HoldId { get; private set; }

    public CardTopupOutcome Outcome { get; private set; }

    /// <summary>Ödendiyse paranın cüzdana yazıldığı ledger işlemi; ödenmediyse yok.</summary>
    public Guid? LedgerTransactionId { get; private set; }

    public DateTimeOffset ClosedAt { get; private set; }
}

/// <summary>Kartla yüklemenin sonucu.</summary>
public enum CardTopupOutcome
{
    /// <summary>Kart çekildi, para cüzdana yazıldı.</summary>
    Paid = 1,

    /// <summary>Ödeme olmadı: müşteri vazgeçti, süresi doldu ya da sağlayıcı reddetti.</summary>
    Failed = 2
}

/// <summary>Kolon değeri ve sözleşme aynı metni kullanıyor.</summary>
public static class CardTopupOutcomes
{
    public static string ToText(this CardTopupOutcome outcome)
    {
        return outcome switch
        {
            CardTopupOutcome.Paid => "paid",
            CardTopupOutcome.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Eşlemesi yazılmamış sonuç.")
        };
    }

    public static bool TryFromText(string? text, out CardTopupOutcome outcome)
    {
        switch (text)
        {
            case "paid":
                outcome = CardTopupOutcome.Paid;
                return true;
            case "failed":
                outcome = CardTopupOutcome.Failed;
                return true;
            default:
                outcome = default;
                return false;
        }
    }

    public static CardTopupOutcome FromText(string text) =>
        TryFromText(text, out var outcome)
            ? outcome
            : throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen kartla yükleme sonucu.");
}
