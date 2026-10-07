using HiWallet.CardTopup.Application;

namespace HiWallet.CardTopup.Api.Requests;

/// <param name="ReturnUrl">Ödeme sayfasından sonra müşterinin döneceği adres; arayüz veriyor.</param>
public sealed record StartCardTopupRequest(Guid WalletId, decimal Amount, string Currency, string ReturnUrl)
{
    public StartCardTopupCommand ToCommand(string idempotencyKey, string subject) =>
        new(subject, idempotencyKey, WalletId, Amount, Currency.ToUpperInvariant(), ReturnUrl);
}
