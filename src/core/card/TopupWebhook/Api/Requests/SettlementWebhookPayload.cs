using FluentValidation;

namespace HiWallet.TopupWebhook.Api.Requests;

/// <summary>
/// Sağlayıcının settlement bildiriminin ham hali. Bütün alanlar nullable:
/// "gönderilmedi" ile "sıfır gönderildi" ayrımı doğrulamada gerekiyor ve
/// <c>required</c> olsaydı eksik alan model binding'de değil deserialize'da
/// patlardı — hata mesajı da o kadar açıklayıcı olmazdı.
/// </summary>
public sealed class SettlementWebhookPayload
{
    public string? SettlementId { get; init; }

    public string? Currency { get; init; }

    /// <summary>Kapanan alacağın toplamı; sağlayıcının kestiği ücret dahil.</summary>
    public decimal? GrossAmount { get; init; }

    /// <summary>Sağlayıcının kestiği ücret. Invoiced modelde sıfır gelir.</summary>
    public decimal? FeeAmount { get; init; }

    /// <summary>Banka hesabımıza gerçekten giren tutar.</summary>
    public decimal? NetAmount { get; init; }

    /// <summary>Batch'in kapsadığı sağlayıcı işlem referansları.</summary>
    public IReadOnlyList<string>? References { get; init; }

    public DateTimeOffset? SettledAt { get; init; }
}

public sealed class SettlementWebhookPayloadValidator : AbstractValidator<SettlementWebhookPayload>
{
    /// <summary>
    /// Bir batch'in kapsayabileceği işlem sayısı. Gövde sınırı zaten 32 KB ama
    /// açık bir üst sınır hata mesajını anlaşılır yapıyor.
    /// </summary>
    private const int MaxReferences = 500;

    public SettlementWebhookPayloadValidator()
    {
        RuleFor(p => p.SettlementId)
            .NotEmpty().WithMessage("settlementId zorunlu — idempotency buna dayanıyor.");

        RuleFor(p => p.Currency)
            .NotEmpty().WithMessage("currency zorunlu.")
            .Length(3).WithMessage("currency 3 karakterli ISO 4217 kodu olmalı.");

        RuleFor(p => p.GrossAmount)
            .NotNull().WithMessage("grossAmount zorunlu.")
            .GreaterThan(0m).WithMessage("grossAmount pozitif olmalı.");

        // Ücret SIFIR olabilir (Invoiced model) ama negatif olamaz.
        RuleFor(p => p.FeeAmount)
            .NotNull().WithMessage("feeAmount zorunlu.")
            .GreaterThanOrEqualTo(0m).WithMessage("feeAmount negatif olamaz.");

        RuleFor(p => p.NetAmount)
            .NotNull().WithMessage("netAmount zorunlu.")
            .GreaterThan(0m).WithMessage("netAmount pozitif olmalı.");

        // Brüt = net + ücret. Sağlayıcı tutarsız bir batch gönderdiyse ledger'a
        // HİÇBİR ŞEY yazılmamalı: üç bacaklı kayıt dengeli çıkmaz ve trigger'a
        // kadar gitmesine gerek yok, sınırda reddediliyor.
        RuleFor(p => p)
            .Must(p => p.GrossAmount!.Value == p.NetAmount!.Value + p.FeeAmount!.Value)
            .When(p => p.GrossAmount.HasValue && p.NetAmount.HasValue && p.FeeAmount.HasValue)
            .WithMessage("grossAmount, netAmount + feeAmount toplamına eşit olmalı.")
            .OverridePropertyName(nameof(SettlementWebhookPayload.GrossAmount));

        RuleFor(p => p.References)
            .NotNull().WithMessage("references zorunlu.")
            .Must(refs => refs!.Count > 0).WithMessage("Boş bir batch settlement olamaz.")
            .Must(refs => refs!.Count <= MaxReferences)
            .WithMessage($"Bir batch en fazla {MaxReferences} işlem taşıyabilir.");

        RuleFor(p => p.SettledAt).NotNull().WithMessage("settledAt zorunlu.");
    }
}
