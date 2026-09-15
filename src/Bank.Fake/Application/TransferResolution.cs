using HiWallet.Bank.Fake.Infrastructure.Persistence;

namespace HiWallet.Bank.Fake.Application;

/// <summary>
/// "Bu transferin şu an durumu ne" sorusunun TEK cevabı.
///
/// Tek yerde duruyor çünkü iki tüketicisi var: durum sorgusu ucu ve callback
/// göndericisi. İkisi ayrı ayrı hesaplasaydı, sahte banka aynı transfer için
/// sorguda başka callback'te başka şey söyleyebilirdi — ve o tutarsızlık bizim
/// tarafımızda saga çelişkisi olarak görünürdü (madde 31), oysa hata bankada olurdu.
/// </summary>
internal static class TransferResolution
{
    /// <summary>
    /// Sonuç henüz belli değilse <c>pending</c>. Belli olduysa senaryodan sabitlenmiş
    /// sonuca göre <c>succeeded</c> ya da <c>failed</c>.
    ///
    /// <see cref="TransferOutcome.TransientFailure"/> burada GÖRÜNMÜYOR: o sonuç
    /// transferin hiç kabul edilmemesi demek, yani ortada satır yok.
    /// </summary>
    public static string StatusOf(BankTransfer transfer, DateTimeOffset now)
    {
        if (now < transfer.ResolveAt) return TransferStatus.Pending;

        return TransferOutcomes.FromText(transfer.Outcome) switch
        {
            TransferOutcome.Success or TransferOutcome.DelayedSuccess => TransferStatus.Succeeded,
            TransferOutcome.Failure => TransferStatus.Failed,
            _ => TransferStatus.Pending
        };
    }

    /// <summary>Banka reddettiğinde müşteriye söylediği sebep. Sabit; gerçeğinde kod listesi olurdu.</summary>
    public const string FailureReason = "Alıcı hesap kapalı.";
}
