using HiWallet.WalletService.Application.CardTopups;

namespace HiWallet.WalletApi.Requests;

/// <param name="HoldId">
/// Kart yüklemesinin kimliği; kart yüklemesi servisi veriyor. Idempotency anahtarı da bu:
/// aynı kimlikle ikinci istek yeni pay açmaz, mevcut payı döner.
/// </param>
/// <param name="Provider">Kart sağlayıcısı; para yazılırken clearing hesabı onunki.</param>
public sealed record PlaceCardTopupHoldRequest(Guid HoldId, Guid WalletId, decimal Amount, string Currency, string Provider)
{
    public PlaceCardTopupHoldCommand ToCommand() => new(HoldId, WalletId, Amount, Currency, Provider);
}
