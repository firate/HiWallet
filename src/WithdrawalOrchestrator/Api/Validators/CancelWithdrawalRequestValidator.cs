using FluentValidation;
using HiWallet.WithdrawalOrchestrator.Api.Requests;

namespace HiWallet.WithdrawalOrchestrator.Api.Validators;

public sealed class CancelWithdrawalRequestValidator : AbstractValidator<CancelWithdrawalRequest>
{
    private const int MaxReasonLength = 500;

    public CancelWithdrawalRequestValidator()
    {
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("İptalin sebebi zorunlu.")
            .MaximumLength(MaxReasonLength).WithMessage($"Sebep en fazla {MaxReasonLength} karakter olabilir.");
    }
}
