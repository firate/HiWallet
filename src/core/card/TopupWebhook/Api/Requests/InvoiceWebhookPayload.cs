using FluentValidation;

namespace HiWallet.TopupWebhook.Api.Requests;

public sealed class InvoiceWebhookPayload
{
    public string? InvoiceRef { get; init; }

    public string? Currency { get; init; }

    public decimal? Amount { get; init; }

    /// <summary>
    /// Faturanın kapsadığı işlem referansları. Boş ya da hiç gönderilmemiş olabilir:
    /// bazı sağlayıcılar yalnızca toplam bildiriyor.
    /// </summary>
    public IReadOnlyList<string>? References { get; init; }

    public DateTimeOffset? IssuedAt { get; init; }
}

public sealed class InvoiceWebhookPayloadValidator : AbstractValidator<InvoiceWebhookPayload>
{
    private const int MaxReferences = 5000;

    public InvoiceWebhookPayloadValidator()
    {
        RuleFor(p => p.InvoiceRef)
            .NotEmpty().WithMessage("invoiceRef zorunlu — idempotency buna dayanıyor.");

        RuleFor(p => p.Currency)
            .NotEmpty().WithMessage("currency zorunlu.")
            .Length(3).WithMessage("currency 3 karakterli ISO 4217 kodu olmalı.");

        RuleFor(p => p.Amount)
            .NotNull().WithMessage("amount zorunlu.")
            .GreaterThan(0m).WithMessage("amount pozitif olmalı.");

        // references BOŞ OLABİLİR — yokluğu "kapsam: faturalanmamış hepsi" demek,
        // hata değil. Yalnızca üst sınırı var.
        RuleFor(p => p.References)
            .Must(refs => refs!.Count <= MaxReferences)
            .When(p => p.References is not null)
            .WithMessage($"Bir fatura en fazla {MaxReferences} kalem taşıyabilir.");

        RuleFor(p => p.IssuedAt).NotNull().WithMessage("issuedAt zorunlu.");
    }
}
