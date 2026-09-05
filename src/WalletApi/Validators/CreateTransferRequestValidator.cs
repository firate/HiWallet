using FluentValidation;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletApi.Validators;

/// <summary>
/// Sınırda doğrulama (baseline.md madde 6). Geçersiz girdi domain'e ulaşmadan reddedilir.
///
/// Buradaki kurallar İŞ KURALI DEĞİL, girdi kuralı: yetersiz bakiye ve limit aşımı
/// burada kontrol edilmez, onlar domain'in işi ve <c>422</c> döner. Burası <c>400</c>.
/// </summary>
public sealed class CreateTransferRequestValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferRequestValidator()
    {
        RuleFor(r => r.FromWalletId)
            .NotEmpty().WithMessage("Gönderen cüzdan kimliği zorunlu.");

        RuleFor(r => r.ToWalletId)
            .NotEmpty().WithMessage("Alan cüzdan kimliği zorunlu.")
            .NotEqual(r => r.FromWalletId).WithMessage("Gönderen ve alan cüzdan aynı olamaz.");

        RuleFor(r => r.Amount)
            .GreaterThan(0m).WithMessage("Tutar pozitif olmalı.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Para birimi zorunlu.")
            .Must(BeAValidCurrency)
            .WithMessage("Para birimi 3 büyük harften oluşan ISO 4217 kodu olmalı.");

        // Tutarın ondalık basamağı para birimine bağlı; ikisi de geçerliyse kontrol edilir.
        RuleFor(r => r.Amount)
            .Must((request, amount) => FitsMinorUnit(request.Currency, amount))
            .WithMessage(r => $"Tutar {r.Currency} için izin verilen ondalık basamağı aşıyor.")
            .When(r => r.Amount > 0m && BeAValidCurrency(r.Currency));

        RuleFor(r => r.Type)
            .IsInEnum().WithMessage("Bilinmeyen transfer tipi.");
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

    private static bool FitsMinorUnit(string code, decimal amount)
    {
        var currency = Currency.From(code);
        return decimal.Round(amount, currency.MinorUnit) == amount;
    }
}
