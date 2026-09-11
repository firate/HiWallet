using HiWallet.Shared.Contracts.Actors;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;

namespace HiWallet.WithdrawalOrchestrator.Api.Requests;

/// <param name="AccountId">
/// Idempotency kapsamı. Orchestrator bunu cüzdandan türetemiyor — cüzdan tablosu
/// wallet'ta ve servis sınırı geçilmiyor.
/// </param>
/// <param name="Amount">Müşteriye ulaşacak tutar. Komisyon buna EK olarak düşülür.</param>
/// <param name="DestinationIban">
/// Boşluklu yazılabilir; normalize etmek <see cref="Iban"/>'ın işi.
/// </param>
public sealed record CreateWithdrawalRequest(
    Guid AccountId,
    Guid WalletId,
    decimal Amount,
    string Currency,
    string DestinationIban)
{
    /// <summary>
    /// <c>Iban.From</c> burada güvenle çağrılıyor: doğrulayıcı zaten geçti, geçersiz
    /// bir değer bu noktaya ulaşamıyor. IBAN sınırı BURASI — bu noktadan sonra
    /// akışta string IBAN dolaşmıyor (CLAUDE.md "Withdrawal saga").
    /// </summary>
    public StartWithdrawalCommand ToCommand(string idempotencyKey)
    {
        return new StartWithdrawalCommand(
            AccountId,
            WalletId,
            Amount,
            Currency.ToUpperInvariant(),
            Iban.From(DestinationIban),
            idempotencyKey,
            // Bugün başlatan HER ZAMAN müşteri ve kimliği gövdeden geliyor — authn
            // yok (baseline.md "Opsiyonel Katman A"). Authn geldiğinde burası
            // DOĞRULANMIŞ özneden dolmalı ve gövdedeki AccountId ile eşleştiği
            // kontrol edilmeli; backoffice çağırdığında da `employee` olmalı.
            // Şu haliyle istemci kendi aktörünü beyan ediyor, bu bir güven varsayımı.
            new CommandActor { Type = ActorTypes.Customer, Id = AccountId.ToString() });
    }
}
