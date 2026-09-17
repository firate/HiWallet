using FluentValidation;
using HiWallet.Bank.Fake.Application;

namespace HiWallet.Bank.Fake.Api.Requests;

/// <param name="ClientReference">
/// Müşterinin kendi referansı; bizde saga kimliği. Senaryo bunun üzerinden kuruluyor
/// çünkü test çekimi başlatmadan ÖNCE saga kimliğini biliyor, ama bankaya giden
/// idempotency anahtarını (outbox satır id'si) bilmiyor.
///
/// IBAN'a bağlansaydı aynı hesaba yapılan iki farklı çekim birbirinin senaryosunu
/// bozardı ve paralel testler kullanamazdı.
/// </param>
/// <param name="TransientFailures">
/// Kaç kez geçici hata üretilecek; sonrasında transfer kabul ediliyor.
/// <see cref="TransferOutcome.TransientFailure"/> dışında anlamsız.
/// </param>
/// <param name="DelayMilliseconds">
/// Yalnızca <see cref="TransferOutcome.DelayedSuccess"/>'te kullanılıyor: sonucun
/// belli olması için geçecek süre. Diğer sonuçlarda ve sıfır bırakılırsa servisin
/// <c>SettlementDelay</c> varsayılanı geçerli.
/// </param>
public sealed record ScenarioRequest(
    string ClientReference,
    TransferOutcome Outcome,
    int TransientFailures = 1,
    int DelayMilliseconds = 0);

public sealed class ScenarioRequestValidator : AbstractValidator<ScenarioRequest>
{
    /// <summary>
    /// Gecikme üst sınırı. Sahte servis testlerin süresini belirliyor; sınırsız
    /// bırakılsaydı yanlış bir değer koşuyu dakikalarca asardı.
    /// </summary>
    private const int MaxDelayMilliseconds = 30_000;

    public ScenarioRequestValidator()
    {
        RuleFor(r => r.ClientReference).NotEmpty().WithMessage("Müşteri referansı zorunlu.");

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
