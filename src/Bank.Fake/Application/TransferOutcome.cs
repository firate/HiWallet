namespace HiWallet.Bank.Fake.Application;

/// <summary>
/// Sahte bankanın bir transfere vereceği sonuç. Senaryo kurulmamışsa
/// <see cref="Success"/>.
///
/// Sayılar sabit ama veritabanında METİN duruyor: enum değerinin kayması eski
/// satırların anlamını değiştirirdi. Proje bu kalıbı saga durumlarında ve inbox
/// tipinde de uyguluyor.
/// </summary>
public enum TransferOutcome
{
    /// <summary>Kabul edilir ve hemen sonuçlanır.</summary>
    Success = 1,

    /// <summary>
    /// Kabul edilir ama sonuç KALICI olarak başarısız. Adaptör bunu
    /// <c>BankTransferFailed</c>'a çevirip saga'yı telafiye sokuyor.
    /// </summary>
    Failure = 2,

    /// <summary>
    /// Transfer HİÇ kabul edilmiyor: banka o an cevap veremiyor ve <c>503</c>
    /// dönüyor. Kalıcı başarısızlıktan farkı, ortada bir transfer kaydının
    /// OLUŞMAMASI — adaptörün yeniden denemesi bekleniyor.
    ///
    /// Bu ayrım sahte bankanın en önemli özelliği: "banka reddetti" ile "bankaya
    /// ulaşamadım" karıştırılırsa her ağ kesintisi müşterinin parasını ileri geri
    /// taşır (overview.md madde 6).
    /// </summary>
    TransientFailure = 3,

    /// <summary>
    /// Kabul edilir, sonuç GECİKMELİ olarak başarılı. Gerçek havalenin normal hali
    /// bu; asenkron modelin görünür olması için var.
    /// </summary>
    DelayedSuccess = 4
}

public static class TransferOutcomes
{
    /// <summary>
    /// Kolona yazılan değer. <c>ToString()</c> YETMEZ: enum yeniden adlandırıldığında
    /// şema sessizce ayrışırdı.
    /// </summary>
    public static string ToText(this TransferOutcome outcome) => outcome switch
    {
        TransferOutcome.Success => "success",
        TransferOutcome.Failure => "failure",
        TransferOutcome.TransientFailure => "transient_failure",
        TransferOutcome.DelayedSuccess => "delayed_success",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Eşlemesi yazılmamış sonuç.")
    };

    public static TransferOutcome FromText(string text) => text switch
    {
        "success" => TransferOutcome.Success,
        "failure" => TransferOutcome.Failure,
        "transient_failure" => TransferOutcome.TransientFailure,
        "delayed_success" => TransferOutcome.DelayedSuccess,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen sonuç.")
    };
}

/// <summary>
/// Bankanın transfer için verdiği durum. Bu, dışarıya (adaptöre) görünen tek
/// durum kümesi — <see cref="TransferOutcome"/> bankanın İÇ senaryosu, bu ise
/// API'nin sözleşmesi. İkisi ayrı tutuluyor: senaryo bankanın kendi bilgisi,
/// durum ise müşterisine söylediği şey.
/// </summary>
public static class TransferStatus
{
    /// <summary>Kabul edildi, sonuç henüz belli değil.</summary>
    public const string Pending = "pending";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";
}
