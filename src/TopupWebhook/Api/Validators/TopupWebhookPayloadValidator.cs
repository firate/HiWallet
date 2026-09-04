using FluentValidation;
using HiWallet.TopupWebhook.Api.Requests;

namespace HiWallet.TopupWebhook.Api.Validators;

/// <summary>
/// Yalnızca ŞEKİL doğrulaması: alan var mı, tipi tutuyor mu, aralıkta mı.
/// "Bu cüzdan var mı", "para birimi cüzdanınkiyle aynı mı" gibi sorular burada
/// sorulmuyor — onlar wallet-service'in bilgisi ve tüketici tarafında kontrol
/// ediliyor. Webhook'un işi mesajı kaybetmeden almak.
/// </summary>
public sealed class TopupWebhookPayloadValidator : AbstractValidator<TopupWebhookPayload>
{
    public TopupWebhookPayloadValidator()
    {
        RuleFor(p => p.EventId)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(p => p.WalletId)
            .NotNull()
            .Must(id => id != Guid.Empty)
            .WithMessage("walletId boş olamaz.");

        RuleFor(p => p.Amount)
            .NotNull()
            .GreaterThan(0m);

        RuleFor(p => p.Currency)
            .NotEmpty()
            .Length(3);

        RuleFor(p => p.Reference)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(p => p.OccurredAt)
            .NotNull();
    }
}
