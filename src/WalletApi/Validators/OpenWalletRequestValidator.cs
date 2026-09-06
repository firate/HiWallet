using FluentValidation;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletApi.Validators;

/// <summary>
/// Girdi kuralı, iş kuralı değil. "Bu para biriminde sistem hesabı var mı" sorusu
/// BURADA sorulmuyor — o ledger'ın bilgisi ve handler'da <c>422</c> üretiyor.
/// </summary>
public sealed class OpenWalletRequestValidator : AbstractValidator<OpenWalletRequest>
{
    private const int MaxNameLength = 100;

    public OpenWalletRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Cüzdan adı zorunlu.")
            .MaximumLength(MaxNameLength)
            .WithMessage($"Cüzdan adı en fazla {MaxNameLength} karakter olabilir.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Para birimi zorunlu.")
            .Must(BeAValidCurrency)
            .WithMessage("Para birimi 3 büyük harften oluşan ISO 4217 kodu olmalı.");
    }

    private static bool BeAValidCurrency(string? code)
    {
        if (code is null)
        {
            return false;
        }

        try
        {
            Currency.From(code);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
