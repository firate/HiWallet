using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;

namespace HiWallet.WithdrawalOrchestrator.Api.Responses;

/// <summary>
/// <c>POST /v1/withdrawals</c> response'u. Verilen söz "para gönderildi" DEĞİL,
/// "request'i kalıcı olarak aldım" — bu yüzden <c>202</c> (decisions.md madde 29 ile
/// aynı gerekçe). Sonucu öğrenmek için <c>Location</c> takip edilir.
/// </summary>
/// <param name="Replayed">
/// Aynı idempotency anahtarıyla daha önce başlatılmış. Yeni çekim AÇILMADI.
/// </param>
public sealed record WithdrawalAcceptedResponse(Guid WithdrawalId, string State, bool Replayed)
{
    public static WithdrawalAcceptedResponse From(StartWithdrawalResult result) =>
        new(result.WithdrawalId, result.State.ToText(), result.Replayed);
}

/// <summary>
/// <c>GET /v1/withdrawals/{id}</c> response'u.
/// </summary>
/// <param name="TotalDebited">
/// Cüzdandan gerçekte çıkan toplam (tutar + komisyon). Wallet düşmeyi yapana kadar
/// <c>null</c>; 0 yazılmıyor, "komisyonsuz çekildi" ile karışırdı.
/// </param>
public sealed record WithdrawalResponse(
    Guid WithdrawalId,
    string State,
    decimal Amount,
    string Currency,
    string DestinationIban,
    decimal? TotalDebited,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static WithdrawalResponse From(WithdrawalSaga saga) =>
        new(
            saga.Id,
            saga.State.ToText(),
            saga.Amount,
            saga.Currency,
            // Maskeli. Müşteri IBAN'ı zaten kendi girdi; tam hali response'ta dolaşınca
            // log'a, hata izlemeye ve tarayıcı geçmişine de düşüyor.
            saga.Destination.Masked,
            saga.TotalDebited,
            saga.FailureReason,
            saga.CreatedAt,
            saga.UpdatedAt);
}
