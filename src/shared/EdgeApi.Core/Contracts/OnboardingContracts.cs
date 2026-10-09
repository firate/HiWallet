namespace HiWallet.EdgeApi.Contracts;

public sealed record StartRegistrationRequest(string Email);

public sealed record RegistrationStartedResponse(Guid RegistrationId, DateTimeOffset CodeExpiresAt);

public sealed record VerifyCodeRequest(string Code);

public sealed record EmailVerifiedResponse(bool EmailVerified);

/// <param name="Password">Kimlik sağlayıcıya yazılıyor; ne ön API ne onboarding saklıyor.</param>
public sealed record CompleteRegistrationRequest(string Password);

public sealed record RegistrationCompletedResponse(Guid AccountId, string Email);

public sealed record DocumentsResponse(string TermsVersion, string PrivacyNoticeVersion);

/// <param name="Phone">Maskeli; telefon doğrulanmadıysa <c>null</c>.</param>
public sealed record OnboardingStatusResponse(
    string? Email,
    string? Phone,
    bool PhoneVerified,
    bool IdentityVerified,
    bool BasicVerificationCompleted,
    DocumentsResponse Documents);

public sealed record StartPhoneVerificationRequest(string Phone);

/// <param name="Phone">Maskeli.</param>
public sealed record PhoneVerificationStartedResponse(Guid VerificationId, string Phone, DateTimeOffset ExpiresAt);

public sealed record ConfirmPhoneRequest(string Code);

public sealed record PhoneVerifiedResponse(string Phone);

/// <param name="Phone">Yeni numara, maskeli.</param>
/// <param name="WithdrawalHoldUntil">Bankaya çekimin açılacağı an; tekrar edilen onayda <c>null</c>.</param>
public sealed record PhoneChangedResponse(string Phone, DateTimeOffset? WithdrawalHoldUntil);

public sealed record IdentityRequest(string FirstName, string LastName, string NationalId, DateOnly BirthDate);

/// <param name="NationalId">Maskeli.</param>
public sealed record IdentityVerifiedResponse(string NationalId);

public sealed record BasicVerificationRequest(string TermsVersion, string PrivacyNoticeVersion);

public sealed record BasicVerificationResponse(Guid AccountId, string KycLevel);
