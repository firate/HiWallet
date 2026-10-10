using FluentValidation;
using HiWallet.WithdrawalOrchestrator.Api.Requests;

namespace HiWallet.WithdrawalOrchestrator.Api.Validators;

public sealed class StartDepositReturnRequestValidator : AbstractValidator<StartDepositReturnRequest>
{
    public StartDepositReturnRequestValidator()
    {
        RuleFor(r => r.SuspendedDepositId).NotEmpty().WithMessage("Havalenin kimliği zorunlu.");
    }
}
