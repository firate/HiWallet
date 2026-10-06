namespace HiWallet.CardTopup.Domain;

/// <summary>Kartla yüklemenin durumu.</summary>
public enum CardTopupState
{
    /// <summary>Kayıt açıldı; wallet'tan limit payı henüz alınmadı.</summary>
    Created = 1,

    /// <summary>Limit payı ayrıldı, ödeme sağlayıcıda açık; müşteri kartını giriyor.</summary>
    Pending = 2,

    /// <summary>Kart çekildi; wallet'a para yazdırıldı. Son.</summary>
    Paid = 3,

    /// <summary>Ödeme olmadı: müşteri vazgeçti, süresi doldu, açılamadı. Pay serbest. Son.</summary>
    Failed = 4,

    /// <summary>Wallet payı vermedi (seviye limiti, cüzdan yok); ödeme hiç açılmadı. Son.</summary>
    Rejected = 5
}

public static class CardTopupStates
{
    public static bool IsTerminal(this CardTopupState state) =>
        state is CardTopupState.Paid or CardTopupState.Failed or CardTopupState.Rejected;

    /// <summary>Kolon değeri ve API gövdesi aynı metni kullanıyor.</summary>
    public static string ToText(this CardTopupState state) => state switch
    {
        CardTopupState.Created => "created",
        CardTopupState.Pending => "pending",
        CardTopupState.Paid => "paid",
        CardTopupState.Failed => "failed",
        CardTopupState.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Eşlemesi yazılmamış durum.")
    };

    public static CardTopupState FromText(string text) => text switch
    {
        "created" => CardTopupState.Created,
        "pending" => CardTopupState.Pending,
        "paid" => CardTopupState.Paid,
        "failed" => CardTopupState.Failed,
        "rejected" => CardTopupState.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen durum.")
    };
}

/// <summary>
/// Geçişin sonucu. Zararsız tekrar (<see cref="Ignored"/>) ile para kaybına işaret eden
/// çelişki (<see cref="Conflict"/>) ayrı: çekim saga'sındaki ayrımın aynısı
/// (decisions.md madde 31). Çelişkide durum değişmiyor, alarm üretiliyor.
/// </summary>
public enum TransitionResult
{
    Applied = 1,
    Ignored = 2,
    Conflict = 3
}
