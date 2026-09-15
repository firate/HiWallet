namespace HiWallet.Bank.Fake.Infrastructure.Callbacks;

/// <summary>
/// Bankanın bize gönderdiği sonuç bildirimi. BANKANIN sözleşmesi — bizim
/// <c>Shared.Contracts</c>'ımızdan gelmiyor ve gelmemeli (decisions.md madde 35).
/// </summary>
public sealed record CallbackNotification
{
    /// <summary>
    /// Bankanın event kimliği. <b>Tekrar denemelerde AYNI kalmak zorunda</b> — bizim
    /// tarafımızdaki inbox <c>(provider, event_id)</c> ile deduplike ediyor. Her
    /// denemede yeni üretilseydi callback iki kez ulaştığında iki kez işlenirdi.
    ///
    /// Bu yüzden rastgele değil, transferin referansından TÜRETİLİYOR: bir transferin
    /// tek bir terminal sonucu var.
    /// </summary>
    public required string EventId { get; init; }

    public required string BankReference { get; init; }

    /// <summary>Bizim gönderdiğimiz referans; saga kimliği. Korelasyon bunun üzerinden.</summary>
    public required string ClientReference { get; init; }

    /// <summary><c>succeeded</c> ya da <c>failed</c>. <c>pending</c> callback'i gönderilmiyor.</summary>
    public required string Status { get; init; }

    public required decimal Fee { get; init; }

    public required string Currency { get; init; }

    /// <summary>Yalnızca <c>failed</c> durumunda dolu.</summary>
    public string? FailureReason { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
