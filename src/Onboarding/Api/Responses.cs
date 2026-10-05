using HiWallet.Onboarding.Application;

namespace HiWallet.Onboarding.Api;

public sealed record RegistrationStartedResponse(Guid RegistrationId, DateTimeOffset CodeExpiresAt);

public sealed record EmailVerifiedResponse(bool EmailVerified);

public sealed record RegistrationCompletedResponse(Guid AccountId, string Email);

/// <param name="Phone">Maskeli.</param>
public sealed record PhoneVerificationStartedResponse(Guid VerificationId, string Phone, DateTimeOffset ExpiresAt);

/// <param name="Phone">Maskeli.</param>
public sealed record PhoneVerifiedResponse(string Phone);

/// <param name="NationalId">Maskeli.</param>
public sealed record IdentityVerifiedResponse(string NationalId);

public sealed record BasicVerificationResponse(Guid AccountId, string KycLevel);

public sealed record DocumentsResponse(string TermsVersion, string PrivacyNoticeVersion);

/// <param name="Phone">Maskeli; telefon doğrulanmadıysa <c>null</c>.</param>
public sealed record OnboardingStatusResponse(
    string? Email,
    string? Phone,
    bool PhoneVerified,
    bool IdentityVerified,
    bool BasicVerificationCompleted,
    DocumentsResponse Documents)
{
    public static OnboardingStatusResponse From(OnboardingStatus status) => new(
        status.Email,
        status.Phone?.Masked,
        status.PhoneVerified,
        status.IdentityVerified,
        status.BasicVerificationCompleted,
        new DocumentsResponse(status.Documents.Terms, status.Documents.PrivacyNotice));
}

/// <param name="Matches">Kimlik numarası sahibin doğrulanmış numarası mı.</param>
public sealed record HolderCheckResponse(bool Matches);
