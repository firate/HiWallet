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

/// <summary>Tek ölçüt: e-posta, telefon ya da kimlik numarası. Kuralı onboarding uyguluyor.</summary>
public sealed record CustomerSearchRequest(string? Email, string? Phone, string? NationalId);

/// <param name="Phone">Maskeli.</param>
public sealed record CustomerMatchResponse(Guid AccountId, string Email, string? FirstName, string? LastName, string? Phone);

public sealed record CustomerSearchResponse(IReadOnlyList<CustomerMatchResponse> Items);

/// <param name="Document">Onaylanan metin: <c>Terms</c> ya da <c>PrivacyNotice</c>.</param>
public sealed record ConsentResponse(string Document, string Version, DateTimeOffset AcceptedAt);

/// <param name="OldPhone">Maskeli; ilk numarası olmayan müşteride <c>null</c>.</param>
/// <param name="NewPhone">Maskeli.</param>
public sealed record PhoneChangeResponse(string? OldPhone, string NewPhone, DateTimeOffset ChangedAt);

/// <summary>Hesabın sahibi, çalışanın gördüğü haliyle; kimlik numarası ve telefon maskeli.</summary>
public sealed record CustomerProfileResponse(
    Guid AccountId,
    string Email,
    string? FirstName,
    string? LastName,
    string? NationalId,
    DateOnly? BirthDate,
    string? Phone,
    DateTimeOffset? PhoneVerifiedAt,
    DateTimeOffset? IdentityVerifiedAt,
    DateTimeOffset? BasicVerifiedAt,
    IReadOnlyList<ConsentResponse> Consents,
    IReadOnlyList<PhoneChangeResponse> PhoneChanges);
