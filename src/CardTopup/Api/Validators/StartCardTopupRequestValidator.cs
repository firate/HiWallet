using FluentValidation;
using HiWallet.CardTopup.Api.Requests;

namespace HiWallet.CardTopup.Api.Validators;

/// <summary>
/// Sınırda girdi kuralı. Para biriminin ondalık basamağı, cüzdanın varlığı ve limit wallet'ın
/// bilgisi; onları pay isteği soruyor.
/// </summary>
public sealed class StartCardTopupRequestValidator : AbstractValidator<StartCardTopupRequest>
{
    public StartCardTopupRequestValidator()
    {
        RuleFor(r => r.WalletId).NotEmpty().WithMessage("Cüzdan kimliği zorunlu.");

        RuleFor(r => r.Amount)
            .GreaterThan(0m).WithMessage("Tutar pozitif olmalı.")
            .Must(amount => amount.Scale <= 4).WithMessage("Tutar en fazla dört ondalık basamak taşıyabilir.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Para birimi zorunlu.")
            .Matches("^[A-Za-z]{3}$").WithMessage("Para birimi üç harfli ISO 4217 kodu olmalı.");

        RuleFor(r => r.ReturnUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                         && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Dönüş adresi mutlak bir http(s) adresi olmalı.");
    }
}
