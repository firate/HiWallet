using FluentValidation;
using HiWallet.WalletApi.Requests;

namespace HiWallet.WalletApi.Validators;

public sealed class OpenPersonAccountRequestValidator : AbstractValidator<OpenPersonAccountRequest>
{
    public OpenPersonAccountRequestValidator()
    {
        RuleFor(r => r.Holder)
            .NotEmpty().WithMessage("Hesabın sahibi olan kimlik zorunlu.")
            .MaximumLength(255);
    }
}

public sealed class RaiseKycLevelRequestValidator : AbstractValidator<RaiseKycLevelRequest>
{
    public RaiseKycLevelRequestValidator()
    {
        // Tanınmayan değer sessizce varsayılana (0) çökmesin.
        RuleFor(r => r.Level)
            .IsInEnum().WithMessage("Seviye Unknown, Unverified, Verified veya Contracted olmalı.");
    }
}
