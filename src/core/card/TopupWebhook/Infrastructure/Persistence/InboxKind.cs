namespace HiWallet.TopupWebhook.Infrastructure.Persistence;

/// <summary>
/// Inbox satırının hangi akışa ait olduğu. Relay yayınlayacağı exchange'i buradan
/// seçiyor.
///
/// Sayılar sabit ama veritabanında METİN duruyor: enum değerinin kayması eski
/// satırların anlamını değiştirirdi.
/// </summary>
public enum InboxKind
{
    /// <summary>Para girişi bildirimi; cüzdan bazında partition'lanıyor.</summary>
    Topup = 1,

    /// <summary>
    /// Sağlayıcının batch ödemesi. Hiçbir cüzdana dokunmuyor — yalnızca sistem
    /// hesapları hareket ediyor.
    /// </summary>
    Settlement = 2
}

public static class InboxKinds
{
    /// <summary>
    /// Kolona yazılan değer. <c>ToString()</c> YETMEZ: enum yeniden adlandırıldığında
    /// şema sessizce ayrışırdı — saga durumlarında da aynı gerekçe var.
    /// </summary>
    public static string ToText(this InboxKind kind) => kind switch
    {
        InboxKind.Topup => "topup",
        InboxKind.Settlement => "settlement",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Eşlemesi yazılmamış inbox tipi.")
    };

    public static InboxKind FromText(string text) => text switch
    {
        "topup" => InboxKind.Topup,
        "settlement" => InboxKind.Settlement,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen inbox tipi.")
    };
}
