using FluentValidation;
using HiWallet.WalletApi.Requests;

namespace HiWallet.WalletApi.Validators;

public sealed class SetDefaultWalletRequestValidator : AbstractValidator<SetDefaultWalletRequest>
{
    public SetDefaultWalletRequestValidator()
    {
        RuleFor(r => r.WalletId).NotEmpty().WithMessage("Cüzdan kimliği zorunlu.");
    }
}
