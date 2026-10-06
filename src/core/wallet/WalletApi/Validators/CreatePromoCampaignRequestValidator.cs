using FluentValidation;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Application.Abstractions;

namespace HiWallet.WalletApi.Validators;

/// <summary>
/// Yalnızca tek tek alanların biçimi. Alanların birbiriyle uyumu (kurala göre eşik,
/// ödüle göre tutar ya da oran, kapsama göre işyeri listesi) <c>PromoCampaign.Create</c>'te;
/// iki kopya olsaydı biri değişince diğeri eski kuralla kalırdı.
/// </summary>
public sealed class CreatePromoCampaignRequestValidator : AbstractValidator<CreatePromoCampaignRequest>
{
    private const int MaxNameLength = 200;

    /// <summary>
    /// Değerlendirme geriye bakıyor; geçmişte başlayan kampanya açılışından önceki
    /// ödemeleri ödüllendirirdi.
    /// </summary>
    private static readonly TimeSpan StartTolerance = TimeSpan.FromMinutes(5);

    public CreatePromoCampaignRequestValidator(IClock clock)
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Kampanyanın adı zorunlu.")
            .MaximumLength(MaxNameLength).WithMessage($"Ad en fazla {MaxNameLength} karakter olabilir.");

        RuleFor(r => r.Rule)
            .Must(PromoTextRules.IsRule)
            .WithMessage("Kural payment_to_merchant ya da daily_payment_total olmalı.");

        RuleFor(r => r.RewardType)
            .Must(PromoTextRules.IsRewardType)
            .WithMessage("Ödül fixed ya da percentage olmalı.");

        RuleFor(r => r.GrantScope)
            .Must(PromoTextRules.IsScope)
            .WithMessage("Kapsam all_businesses ya da selected_businesses olmalı.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Para birimi zorunlu.")
            .Must(CurrencyRules.IsValid)
            .WithMessage("Para birimi 3 büyük harften oluşan ISO 4217 kodu olmalı.");

        RuleFor(r => r.GrantValidForDays)
            .GreaterThan(0).WithMessage("Geçerlilik süresi pozitif olmalı.")
            .When(r => r.GrantValidForDays is not null);

        RuleFor(r => r.StartsAt)
            .Must(startsAt => startsAt >= clock.UtcNow - StartTolerance)
            .WithMessage("Başlangıç geçmişte olamaz.");
    }
}
