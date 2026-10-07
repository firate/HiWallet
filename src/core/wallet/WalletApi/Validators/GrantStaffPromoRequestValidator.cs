using FluentValidation;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Application.Abstractions;

namespace HiWallet.WalletApi.Validators;

/// <summary>
/// Girdi kuralları → <c>400</c>. Tavan, alan hesabın bireysel olması ve kapsamdaki
/// hesapların işyeri olması iş kuralı; handler'da <c>422</c>.
/// </summary>
public sealed class GrantStaffPromoRequestValidator : AbstractValidator<GrantStaffPromoRequest>
{
    public GrantStaffPromoRequestValidator(IClock clock)
    {
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

        RuleFor(r => r.Scope)
            .Must(PromoTextRules.IsScope)
            .WithMessage("Kapsam all_businesses ya da selected_businesses olmalı.");

        RuleFor(r => r.MerchantAccountIds)
            .NotEmpty().WithMessage("Seçili işyerleri kapsamında en az bir işyeri gerekli.")
            .When(r => r.Scope == "selected_businesses");

        RuleFor(r => r.MerchantAccountIds)
            .Empty().WithMessage("Her yerde geçerli kapsamda işyeri listesi boş olmalı.")
            .When(r => r.Scope == "all_businesses");

        RuleFor(r => r.ExpiresAt)
            .Must(expiresAt => expiresAt > clock.UtcNow)
            .WithMessage("Bitiş tarihi gelecekte olmalı.")
            .When(r => r.ExpiresAt is not null);
    }
}
