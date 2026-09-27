namespace HiWallet.EdgeApi.Contracts;

/// <param name="FunderWalletId">İşyerinin cüzdanı. Promo bu cüzdanın cash kovasından çıkıyor.</param>
/// <param name="WalletId">Promo'yu alan müşterinin cüzdanı.</param>
/// <param name="ExpiresAt">Opsiyonel. Verilmezse parti süresiz.</param>
public sealed record GrantPromoRequest(
    Guid FunderWalletId,
    Guid WalletId,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

/// <param name="Replayed">
/// <c>true</c> ise bu <c>Idempotency-Key</c> daha önce işlenmişti; yeni parti açılmadı.
/// </param>
public sealed record PromoGrantResponse(Guid GrantId, bool Replayed);
