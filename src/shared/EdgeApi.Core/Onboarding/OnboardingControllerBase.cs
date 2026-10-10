using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.EdgeApi.Onboarding;

/// <summary>
/// Temel doğrulama, giriş yaptıktan sonra: telefon, kimlik bilgileri, onaylar.
/// Müşterinin token'ı onboarding'e aynen iletiliyor; kimliği oradan okunuyor.
///
/// Soyut: gerekçe <see cref="RegistrationsControllerBase"/>'te.
/// </summary>
public abstract class OnboardingControllerBase(OnboardingClient onboarding) : ControllerBase
{
    /// <summary>Doğrulamanın hangi adımda olduğu ve onaylanacak metinlerin güncel sürümleri.</summary>
    [HttpGet("onboarding")]
    public Task<OnboardingStatusResponse> Status(CancellationToken ct) =>
        onboarding.GetAsync<OnboardingStatusResponse>("v1/me/onboarding", ct);

    /// <summary>Telefona altı haneli kod gönderir.</summary>
    [HttpPost("phone-verifications")]
    [ProducesResponseType<PhoneVerificationStartedResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartPhone([FromBody] StartPhoneVerificationRequest request, CancellationToken ct) =>
        Accepted(await onboarding.PostAsync<PhoneVerificationStartedResponse>(
            "v1/me/phone-verifications", request, null, ct));

    [HttpPost("phone-verifications/{verificationId:guid}/confirmation")]
    public Task<PhoneVerifiedResponse> ConfirmPhone(
        Guid verificationId, [FromBody] ConfirmPhoneRequest request, CancellationToken ct) =>
        onboarding.PostAsync<PhoneVerifiedResponse>(
            $"v1/me/phone-verifications/{verificationId}/confirmation", request, null, ct);

    /// <summary>
    /// Temel doğrulamadan sonra numara değişikliği: yeni numaraya kod. Parolayla yakın zamanda
    /// giriş istiyor; yoksa onboarding'in <c>403</c>'ü (<c>reauthentication_required</c>) aynen.
    /// </summary>
    [HttpPost("phone-changes")]
    [ProducesResponseType<PhoneVerificationStartedResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartPhoneChange([FromBody] StartPhoneVerificationRequest request, CancellationToken ct) =>
        Accepted(await onboarding.PostAsync<PhoneVerificationStartedResponse>(
            "v1/me/phone-changes", request, null, ct));

    /// <summary>Yeni numaranın kodunu doğrular, numarayı değiştirir; bankaya çekim bir süre kapanıyor.</summary>
    [HttpPost("phone-changes/{verificationId:guid}/confirmation")]
    public Task<PhoneChangedResponse> ConfirmPhoneChange(
        Guid verificationId, [FromBody] ConfirmPhoneRequest request, CancellationToken ct) =>
        onboarding.PostAsync<PhoneChangedResponse>(
            $"v1/me/phone-changes/{verificationId}/confirmation", request, null, ct);

    /// <summary>Kimlik bilgilerini nüfus kaydıyla karşılaştırır.</summary>
    [HttpPut("identity")]
    public Task<IdentityVerifiedResponse> Identity([FromBody] IdentityRequest request, CancellationToken ct) =>
        onboarding.PutAsync<IdentityVerifiedResponse>("v1/me/identity", request, ct);

    /// <summary>Onaylar; telefon ve kimlik doğrulandıysa hesap Unverified'a geçiyor.</summary>
    [HttpPost("basic-verification")]
    public Task<BasicVerificationResponse> BasicVerification(
        [FromBody] BasicVerificationRequest request, CancellationToken ct) =>
        onboarding.PostAsync<BasicVerificationResponse>("v1/me/basic-verification", request, null, ct);
}
