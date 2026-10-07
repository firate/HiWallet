using HiWallet.WalletService.Application.Promos;

namespace HiWallet.WalletApi.Responses;

/// <param name="Replayed">
/// <c>true</c> ise bu <c>Idempotency-Key</c> daha önce işlenmişti; yeni parti
/// açılmadı, mevcut partinin kimliği dönüyor.
/// </param>
public sealed record PromoGrantResponse(Guid GrantId, bool Replayed)
{
    public static PromoGrantResponse From(GrantPromoResult result)
    {
        return new PromoGrantResponse(result.GrantId, result.Replayed);
    }
}
