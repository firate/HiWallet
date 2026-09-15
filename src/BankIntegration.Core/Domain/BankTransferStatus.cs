namespace HiWallet.BankIntegration.Domain;

/// <summary>
/// Bizim tarafımızda bir banka transferinin durumu.
///
/// <b>Bankanın durum kümesiyle aynı olması tesadüf değil ama aynı şey değil:</b>
/// bu bizim kaydımız, o bankanın. Bankanın sözleşmesi değişirse eşleme
/// <c>BankClient</c>'ta kırılır, burada değil — sınırın anlamı bu.
/// </summary>
public enum BankTransferStatus
{
    /// <summary>
    /// Banka komutu kabul etti, sonuç henüz belli değil. Saga bu sırada gerçekten
    /// <c>bank_transfer_pending</c>'de bekliyor (decisions.md madde 35).
    /// </summary>
    Pending = 1,

    Succeeded = 2,

    Failed = 3
}

public static class BankTransferStatuses
{
    /// <summary>
    /// Kolona yazılan değer. <c>ToString()</c> YETMEZ: enum yeniden adlandırıldığında
    /// şema sessizce ayrışırdı — saga durumlarında da aynı gerekçe var.
    /// </summary>
    public static string ToText(this BankTransferStatus status) => status switch
    {
        BankTransferStatus.Pending => "pending",
        BankTransferStatus.Succeeded => "succeeded",
        BankTransferStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Eşlemesi yazılmamış durum.")
    };

    public static BankTransferStatus FromText(string text) => text switch
    {
        "pending" => BankTransferStatus.Pending,
        "succeeded" => BankTransferStatus.Succeeded,
        "failed" => BankTransferStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen durum.")
    };

    public static bool IsTerminal(this BankTransferStatus status) =>
        status is BankTransferStatus.Succeeded or BankTransferStatus.Failed;
}
