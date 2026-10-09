namespace HiWallet.Onboarding.Application;

/// <summary>
/// İş kuralı reddi → <c>422</c>. <see cref="Rule"/> gövdede makinenin okuyacağı ad;
/// uygulama müşteriye ne göstereceğini ona göre seçiyor.
/// </summary>
public sealed class OnboardingRuleException(string rule, string message) : Exception(message)
{
    public string Rule { get; } = rule;
}

/// <summary>Kayıt ya da doğrulama yok, veya çağıranın değil → <c>404</c>.</summary>
public sealed class OnboardingNotFoundException(string message) : Exception(message);

/// <summary>Başka bir kayıtla çakışma → <c>409</c>.</summary>
public sealed class OnboardingConflictException(string rule, string message) : Exception(message)
{
    public string Rule { get; } = rule;
}

/// <summary>
/// İşlem parolayla yakın zamanda yapılmış bir giriş istiyor; açık oturum yetmiyor →
/// <c>403</c>, kural <see cref="OnboardingRules.ReauthenticationRequired"/>. Uygulama
/// müşteriyi giriş sayfasına yeniden gönderiyor.
/// </summary>
public sealed class ReauthenticationRequiredException(string message) : Exception(message)
{
    public string Rule => OnboardingRules.ReauthenticationRequired;
}

/// <summary>Makinenin okuyacağı kural adları.</summary>
public static class OnboardingRules
{
    public const string WrongCode = "wrong_code";
    public const string CodeExpired = "code_expired";
    public const string TooManyAttempts = "too_many_attempts";
    public const string EmailNotVerified = "email_not_verified";
    public const string RegistrationExpired = "registration_expired";
    public const string PasswordRejected = "password_rejected";
    public const string EmailRegistered = "email_registered";
    public const string IdentityMismatch = "identity_mismatch";
    public const string NationalIdRegistered = "national_id_registered";
    public const string IdentityLocked = "identity_locked";
    public const string VerificationIncomplete = "verification_incomplete";
    public const string DocumentOutdated = "document_outdated";
    public const string RegistrationMissing = "registration_missing";
    public const string PhoneInUse = "phone_in_use";
    public const string PhoneChangeRequired = "phone_change_required";
    public const string SamePhone = "same_phone";
    public const string ReauthenticationRequired = "reauthentication_required";
}
