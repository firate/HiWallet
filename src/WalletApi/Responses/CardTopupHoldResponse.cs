using HiWallet.WalletService.Application.CardTopups;

namespace HiWallet.WalletApi.Responses;

/// <param name="AccountId">Cüzdanın hesabı; kart yüklemesi servisi kaydında tutuyor.</param>
/// <param name="Replayed">Bu kimlikte pay zaten vardı; yeni pay açılmadı, limit yeniden sorulmadı.</param>
public sealed record CardTopupHoldResponse(
    Guid HoldId, Guid AccountId, Guid WalletId, decimal Amount, string Currency, bool Replayed)
{
    public static CardTopupHoldResponse From(CardTopupHoldResult result) => new(
        result.HoldId,
        result.AccountId,
        result.WalletId,
        result.Amount.Amount,
        result.Amount.Currency.Code,
        result.Replayed);
}
