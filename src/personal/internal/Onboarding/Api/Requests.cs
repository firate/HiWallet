using FluentValidation;
using HiWallet.Onboarding.Domain;

namespace HiWallet.Onboarding.Api;

public sealed record StartRegistrationRequest(string Email);

public sealed record VerifyCodeRequest(string Code);

/// <param name="Password">Kimlik sağlayıcıya yazılıyor; bu servis saklamıyor.</param>
public sealed record CompleteRegistrationRequest(string Password);

public sealed record StartPhoneVerificationRequest(string Phone);

public sealed record ConfirmPhoneRequest(string Code);

public sealed record IdentityRequest(string FirstName, string LastName, string NationalId, DateOnly BirthDate);

/// <param name="TermsVersion">Müşteriye gösterilen kullanıcı sözleşmesinin sürümü.</param>
/// <param name="PrivacyNoticeVersion">Müşteriye gösterilen KVKK aydınlatma metninin sürümü.</param>
public sealed record BasicVerificationRequest(string TermsVersion, string PrivacyNoticeVersion);

/// <param name="Holder">Hesabın sahibi: kimlik sağlayıcıdaki <c>sub</c>.</param>
/// <param name="NationalId">Bankanın bildirdiği gönderen kimlik numarası, olduğu gibi.</param>
public sealed record HolderCheckRequest(string Holder, string NationalId);

/// <summary>Tek ölçüt: e-posta, telefon ya da kimlik numarası.</summary>
public sealed record CustomerSearchRequest(string? Email, string? Phone, string? NationalId);

public sealed class StartRegistrationRequestValidator : AbstractValidator<StartRegistrationRequest>
{
    public StartRegistrationRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254).EmailAddress().WithMessage("Geçerli bir e-posta adresi gir.");
    }
}

public sealed class VerifyCodeRequestValidator : AbstractValidator<VerifyCodeRequest>
{
    public VerifyCodeRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("Kod altı haneli.");
    }
}

public sealed class ConfirmPhoneRequestValidator : AbstractValidator<ConfirmPhoneRequest>
{
    public ConfirmPhoneRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("Kod altı haneli.");
    }
}

public sealed class CompleteRegistrationRequestValidator : AbstractValidator<CompleteRegistrationRequest>
{
    /// <summary>Keycloak'taki parola kuralıyla aynı: <c>length(8)</c>.</summary>
    public const int MinPasswordLength = 8;

    public CompleteRegistrationRequestValidator()
    {
        RuleFor(r => r.Password)
            .NotEmpty()
            .MinimumLength(MinPasswordLength).WithMessage($"Parola en az {MinPasswordLength} karakter olmalı.")
            .MaximumLength(128);
    }
}

public sealed class StartPhoneVerificationRequestValidator : AbstractValidator<StartPhoneVerificationRequest>
{
    public StartPhoneVerificationRequestValidator()
    {
        RuleFor(r => r.Phone)
            .Must(phone => PhoneNumber.TryParse(phone, out _))
            .WithMessage("Türkiye'de bir cep telefonu numarası gir.");
    }
}

public sealed class IdentityRequestValidator : AbstractValidator<IdentityRequest>
{
    public IdentityRequestValidator(TimeProvider time)
    {
        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.NationalId)
            .Must(id => Domain.NationalId.TryParse(id, out _))
            .WithMessage("Geçerli bir T.C. kimlik numarası gir.");

        // E-para hesabı 18 yaş altına açılmıyor.
        RuleFor(r => r.BirthDate)
            .Must(date => date <= DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddYears(-18))
            .WithMessage("18 yaşından küçükler hesap açamıyor.")
            .Must(date => date.Year >= 1900)
            .WithMessage("Doğum tarihi geçersiz.");
    }
}

public sealed class BasicVerificationRequestValidator : AbstractValidator<BasicVerificationRequest>
{
    public BasicVerificationRequestValidator()
    {
        RuleFor(r => r.TermsVersion).NotEmpty();
        RuleFor(r => r.PrivacyNoticeVersion).NotEmpty();
    }
}

/// <summary>
/// Yalnızca varlık ve uzunluk: numaranın kurala uyup uymadığı sorunun kendisi, kurala
/// uymayan numara "hayır" cevabı alıyor, 400 değil.
/// </summary>
public sealed class HolderCheckRequestValidator : AbstractValidator<HolderCheckRequest>
{
    public HolderCheckRequestValidator()
    {
        RuleFor(r => r.Holder).NotEmpty().MaximumLength(255);
        RuleFor(r => r.NationalId).NotEmpty().MaximumLength(32);
    }
}

/// <summary>
/// Tek ölçüt ve geçerli biçimde. Kurala uymayan kimlik numarası ya da telefon aranacak bir
/// şey değil: hiçbir müşteride olamaz, boş liste yerine <c>400</c>.
/// </summary>
public sealed class CustomerSearchRequestValidator : AbstractValidator<CustomerSearchRequest>
{
    public CustomerSearchRequestValidator()
    {
        RuleFor(r => r)
            .Must(r => new[] { r.Email, r.Phone, r.NationalId }.Count(c => c is not null) == 1)
            .WithName("Criterion")
            .WithMessage("Tek ölçüt gir: e-posta, telefon ya da kimlik numarası.");

        RuleFor(r => r.Email)
            .MaximumLength(254).EmailAddress().WithMessage("Geçerli bir e-posta adresi gir.")
            .When(r => r.Email is not null);

        RuleFor(r => r.Phone)
            .Must(phone => PhoneNumber.TryParse(phone, out _))
            .WithMessage("Türkiye'de bir cep telefonu numarası gir.")
            .When(r => r.Phone is not null);

        RuleFor(r => r.NationalId)
            .Must(id => Domain.NationalId.TryParse(id, out _))
            .WithMessage("Geçerli bir T.C. kimlik numarası gir.")
            .When(r => r.NationalId is not null);
    }
}
