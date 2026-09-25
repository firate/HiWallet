using FluentValidation;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Application.Abstractions;

namespace HiWallet.WalletApi.Validators;

/// <summary>
/// Girdi kuralları → <c>400</c>. Fonlayanın işyeri olması ve nakdinin yetmesi iş
/// kuralı; onlar domain'in işi ve <c>422</c> döner.
/// </summary>
public sealed class GrantPromoRequestValidator : AbstractValidator<GrantPromoRequest>
{
    public GrantPromoRequestValidator(IClock clock)
    {
        RuleFor(r => r.FunderWalletId)
            .NotEmpty().WithMessage("Fonlayan cüzdan kimliği zorunlu.");

        RuleFor(r => r.WalletId)
            .NotEmpty().WithMessage("Promo'yu alan cüzdan kimliği zorunlu.")
            .NotEqual(r => r.FunderWalletId).WithMessage("Fonlayan ve alan cüzdan aynı olamaz.");

        RuleFor(r => r.Amount)
            .GreaterThan(0m).WithMessage("Tutar pozitif olmalı.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Para birimi zorunlu.")
            .Must(CurrencyRules.IsValid)
            .WithMessage("Para birimi 3 büyük harften oluşan ISO 4217 kodu olmalı.");

        RuleFor(r => r.Amount)
            .Must((request, amount) => CurrencyRules.FitsMinorUnit(request.Currency, amount))
            .WithMessage(r => $"Tutar {r.Currency} için izin verilen ondalık basamağı aşıyor.")
            .When(r => r.Amount > 0m && CurrencyRules.IsValid(r.Currency));

        RuleFor(r => r.ExpiresAt)
            .Must(expiresAt => expiresAt > clock.UtcNow)
            .WithMessage("Bitiş tarihi gelecekte olmalı.")
            .When(r => r.ExpiresAt is not null);
    }
}
