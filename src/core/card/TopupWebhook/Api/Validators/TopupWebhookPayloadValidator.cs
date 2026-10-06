using FluentValidation;
using HiWallet.Shared.Contracts.CardPayments;
using HiWallet.TopupWebhook.Api.Requests;

namespace HiWallet.TopupWebhook.Api.Validators;

/// <summary>
/// Yalnızca ŞEKİL doğrulaması: alan var mı, tipi tutuyor mu, aralıkta mı. "Bu ödemeyi
/// biz mi açtık", "tutar kaydımızla aynı mı" gibi sorular burada sorulmuyor — onlar kart
/// yüklemesi servisinin bilgisi. Webhook'un işi mesajı kaybetmeden almak.
/// </summary>
public sealed class TopupWebhookPayloadValidator : AbstractValidator<TopupWebhookPayload>
{
    public TopupWebhookPayloadValidator()
    {
        RuleFor(p => p.EventId)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(p => p.Type)
            .Must(CardPaymentEvents.IsKnown)
            .WithMessage($"type '{CardPaymentEvents.Succeeded}' ya da '{CardPaymentEvents.Canceled}' olmalı.");

        RuleFor(p => p.PaymentId)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(p => p.Reference)
            .NotNull()
            .Must(id => id != Guid.Empty)
            .WithMessage("reference boş olamaz.");

        RuleFor(p => p.Amount)
            .NotNull()
            .GreaterThan(0m);

        RuleFor(p => p.Currency)
            .NotEmpty()
            .Length(3);

        RuleFor(p => p.OccurredAt)
            .NotNull();
    }
}
