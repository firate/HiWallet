using FluentValidation;
using HiWallet.WithdrawalOrchestrator.Api.Requests;
using HiWallet.WithdrawalOrchestrator.Domain;

namespace HiWallet.WithdrawalOrchestrator.Api.Validators;

/// <summary>
/// Sınırda doğrulama (baseline.md madde 6). Buradaki kurallar İŞ KURALI DEĞİL, girdi
/// kuralı: yetersiz bakiye ve limit aşımı burada bakılmıyor — onlar wallet'ın işi ve
/// saga <c>Rejected</c> ile bitiyor. Burası <c>400</c>.
/// </summary>
public sealed class CreateWithdrawalRequestValidator : AbstractValidator<CreateWithdrawalRequest>
{
    public CreateWithdrawalRequestValidator()
    {
        RuleFor(r => r.AccountId)
            .NotEmpty().WithMessage("Hesap kimliği zorunlu.");

        RuleFor(r => r.WalletId)
            .NotEmpty().WithMessage("Cüzdan kimliği zorunlu.");

        RuleFor(r => r.Amount)
            .GreaterThan(0m).WithMessage("Tutar pozitif olmalı.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Para birimi zorunlu.")
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("Para birimi 3 harfli ISO 4217 kodu olmalı.");

        // Kuralın taşıyıcısı burası: geçersiz IBAN saga hiç başlamadan eleniyor.
        // "Komisyon koşulsuz iade edilir" sözü buna dayanıyor — müşteri kaynaklı tek
        // gerçekçi başarısızlık sebebi yanlış IBAN ve o buraya takılıyor.
        RuleFor(r => r.DestinationIban)
            .NotEmpty().WithMessage("IBAN zorunlu.")
            .Must(iban => Iban.TryFrom(iban, out _))
            .WithMessage("IBAN geçersiz.");
    }
}
