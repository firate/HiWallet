using HiWallet.WalletService.Application.Promos;

namespace HiWallet.WalletApi.Requests;

/// <param name="FunderWalletId">İşyerinin cüzdanı. Promo'yu bu cüzdanın cash kovası fonluyor.</param>
/// <param name="WalletId">Promo'yu alan cüzdan.</param>
/// <param name="ExpiresAt">Opsiyonel. Verilmezse parti süresiz.</param>
public sealed record GrantPromoRequest(
    Guid FunderWalletId,
    Guid WalletId,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt)
{
    /// <summary><c>Idempotency-Key</c> header'ından gelir, gövdeden değil.</summary>
    public GrantPromoCommand ToCommand(string idempotencyKey)
    {
        return new GrantPromoCommand(FunderWalletId, WalletId, Amount, Currency, ExpiresAt, idempotencyKey);
    }
}
