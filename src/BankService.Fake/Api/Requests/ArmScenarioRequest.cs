using FluentValidation;
using HiWallet.BankService.Application;

namespace HiWallet.BankService.Api.Requests;

/// <param name="SagaId">
/// Senaryo saga başına kuruluyor. IBAN'a bağlansaydı aynı hesaba yapılan iki farklı
/// çekim birbirinin senaryosunu bozardı ve paralel testler kullanamazdı.
/// </param>
/// <param name="TransientFailures">
/// Kaç kez geçici hata üretilecek; sonrasında transfer başarılı oluyor.
/// <see cref="TransferOutcome.TransientFailure"/> dışında anlamsız.
/// </param>
/// <param name="DelayMilliseconds">
/// <see cref="TransferOutcome.DelayedSuccess"/>'te beklenecek süre.
/// </param>
public sealed record ArmScenarioRequest(
    Guid SagaId,
    TransferOutcome Outcome,
    int TransientFailures = 1,
    int DelayMilliseconds = 0);

public sealed class ArmScenarioRequestValidator : AbstractValidator<ArmScenarioRequest>
{
    /// <summary>
    /// Gecikme üst sınırı. Sahte servis testlerin süresini belirliyor; sınırsız
    /// bırakılsaydı yanlış bir değer koşuyu dakikalarca asardı.
    /// </summary>
    private const int MaxDelayMilliseconds = 30_000;

    public ArmScenarioRequestValidator()
    {
        RuleFor(r => r.SagaId).NotEmpty().WithMessage("Saga kimliği zorunlu.");

        RuleFor(r => r.Outcome).IsInEnum().WithMessage("Bilinmeyen senaryo sonucu.");

        RuleFor(r => r.TransientFailures)
            .InclusiveBetween(1, 10)
            .When(r => r.Outcome is TransferOutcome.TransientFailure)
            .WithMessage("Geçici hata sayısı 1-10 aralığında olmalı.");

        RuleFor(r => r.DelayMilliseconds)
            .InclusiveBetween(0, MaxDelayMilliseconds)
            .WithMessage($"Gecikme en fazla {MaxDelayMilliseconds} ms olabilir.");
    }
}
