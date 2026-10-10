using FluentValidation;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Validators;

public sealed class MoveSuspendedDepositRequestValidator : AbstractValidator<MoveSuspendedDepositRequest>
{
    public MoveSuspendedDepositRequestValidator()
    {
        RuleFor(r => r.AccountNumber)
            .Must(number => AccountNumber.TryFrom(number, out _))
            .WithMessage("Hesap numarası geçersiz: on hane ve son hanesi kontrol hanesi.");
    }
}
