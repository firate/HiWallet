using FluentValidation;
using HiWallet.WalletApi.Requests;

namespace HiWallet.WalletApi.Validators;

/// <summary>Sınırda girdi kuralı (baseline.md madde 6). Limit ve hesap kuralları domain'de, <c>422</c>.</summary>
public sealed class PlaceCardTopupHoldRequestValidator : AbstractValidator<PlaceCardTopupHoldRequest>
{
    public PlaceCardTopupHoldRequestValidator()
    {
        RuleFor(r => r.HoldId).NotEmpty().WithMessage("Kartla yüklemenin kimliği zorunlu.");

        RuleFor(r => r.WalletId).NotEmpty().WithMessage("Cüzdan kimliği zorunlu.");

        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("Tutar pozitif olmalı.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Para birimi zorunlu.")
            .Must(CurrencyRules.IsValid)
            .WithMessage("Para birimi 3 büyük harften oluşan ISO 4217 kodu olmalı.");

        RuleFor(r => r.Amount)
            .Must((request, amount) => CurrencyRules.FitsMinorUnit(request.Currency, amount))
            .WithMessage(r => $"Tutar {r.Currency} için izin verilen ondalık basamağı aşıyor.")
            .When(r => r.Amount > 0m && CurrencyRules.IsValid(r.Currency));

        RuleFor(r => r.Provider).NotEmpty().WithMessage("Kart sağlayıcısı zorunlu.");
    }
}
