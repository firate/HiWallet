using HiWallet.Onboarding.Application;
using HiWallet.Onboarding.Domain;

namespace HiWallet.Onboarding.Api;

public sealed record RegistrationStartedResponse(Guid RegistrationId, DateTimeOffset CodeExpiresAt);

public sealed record EmailVerifiedResponse(bool EmailVerified);

public sealed record RegistrationCompletedResponse(Guid AccountId, string Email);

/// <param name="Phone">Maskeli.</param>
public sealed record PhoneVerificationStartedResponse(Guid VerificationId, string Phone, DateTimeOffset ExpiresAt);

/// <param name="Phone">Maskeli.</param>
public sealed record PhoneVerifiedResponse(string Phone);

/// <param name="Phone">Yeni numara, maskeli.</param>
/// <param name="WithdrawalHoldUntil">
/// Bankaya çekimin açılacağı an. Aynı değişikliğin tekrar edilen onayında <c>null</c>.
/// </param>
public sealed record PhoneChangedResponse(string Phone, DateTimeOffset? WithdrawalHoldUntil);

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

/// <summary>
/// Hesabın sahibi, çalışanın gördüğü haliyle. Kimlik numarası ve telefon maskeli: çalışan
/// müşteriyi tanımak için bakıyor, numarayı kopyalamak için değil. Doğrulamaya başlamamış
/// müşteride yalnızca e-posta dolu.
/// </summary>
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
    IReadOnlyList<PhoneChangeResponse> PhoneChanges)
{
    public static CustomerProfileResponse From(CustomerProfile profile)
    {
        var customer = profile.Customer;

        return new CustomerProfileResponse(
            profile.AccountId,
            profile.Email,
            customer?.FirstName,
            customer?.LastName,
            customer?.NationalId?.Masked,
            customer?.BirthDate,
            customer?.Phone?.Masked,
            customer?.PhoneVerifiedAt,
            customer?.IdentityVerifiedAt,
            customer?.BasicVerifiedAt,
            [.. profile.Consents.Select(c => new ConsentResponse(c.Document, c.Version, c.AcceptedAt))],
            [.. profile.PhoneChanges.Select(c => new PhoneChangeResponse(c.OldPhone?.Masked, c.NewPhone.Masked, c.ChangedAt))]);
    }
}

public sealed record ConsentResponse(ConsentDocument Document, string Version, DateTimeOffset AcceptedAt);

/// <param name="OldPhone">Maskeli; ilk numarası olmayan müşteride <c>null</c>.</param>
/// <param name="NewPhone">Maskeli.</param>
public sealed record PhoneChangeResponse(string? OldPhone, string NewPhone, DateTimeOffset ChangedAt);

public sealed record CustomerSearchResponse(IReadOnlyList<CustomerMatchResponse> Items);

/// <param name="Phone">Maskeli.</param>
public sealed record CustomerMatchResponse(Guid AccountId, string Email, string? FirstName, string? LastName, string? Phone)
{
    public static CustomerMatchResponse From(CustomerMatch match) => new(
        match.AccountId,
        match.Email,
        match.Customer?.FirstName,
        match.Customer?.LastName,
        match.Customer?.Phone?.Masked);
}
