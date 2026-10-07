using HiWallet.Onboarding.Application;
using HiWallet.Onboarding.Domain;
using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Onboarding.Api.Controllers;

/// <summary>
/// Temel doğrulama, müşteri giriş yaptıktan sonra. Kimlik token'daki <c>sub</c>:
/// müşteri yalnızca kendi kaydını görüyor ve değiştiriyor.
/// </summary>
[ApiController]
[Route("v1/me")]
public sealed class MeController(VerificationService verification) : ControllerBase
{
    /// <summary>Doğrulamanın hangi adımda olduğu ve onaylanacak metinlerin güncel sürümleri.</summary>
    [HttpGet("onboarding")]
    [ProducesResponseType<OnboardingStatusResponse>(StatusCodes.Status200OK)]
    public async Task<OnboardingStatusResponse> Status(CancellationToken ct) =>
        OnboardingStatusResponse.From(await verification.StatusAsync(User.Subject(), ct));

    /// <summary>Telefona altı haneli kod gönderir.</summary>
    [HttpPost("phone-verifications")]
    [ProducesResponseType<PhoneVerificationStartedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartPhone([FromBody] StartPhoneVerificationRequest request, CancellationToken ct)
    {
        var started = await verification.StartPhoneAsync(User.Subject(), PhoneNumber.Parse(request.Phone), ct);

        return Accepted(new PhoneVerificationStartedResponse(started.VerificationId, started.Phone.Masked, started.ExpiresAt));
    }

    /// <summary>Telefona giden kodu doğrular. Başkasının doğrulaması <c>404</c>.</summary>
    [HttpPost("phone-verifications/{verificationId:guid}/confirmation")]
    [ProducesResponseType<PhoneVerifiedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<PhoneVerifiedResponse> ConfirmPhone(
        Guid verificationId, [FromBody] ConfirmPhoneRequest request, CancellationToken ct)
    {
        var phone = await verification.ConfirmPhoneAsync(User.Subject(), verificationId, request.Code, ct);

        return new PhoneVerifiedResponse(phone.Masked);
    }

    /// <summary>
    /// Kimlik bilgilerini nüfus kaydıyla karşılaştırır. Eşleşmezse <c>422</c>; numara
    /// başka bir müşterideyse <c>409</c>.
    /// </summary>
    [HttpPut("identity")]
    [ProducesResponseType<IdentityVerifiedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IdentityVerifiedResponse> Identity([FromBody] IdentityRequest request, CancellationToken ct)
    {
        var nationalId = await verification.VerifyIdentityAsync(
            User.Subject(),
            request.FirstName.Trim(),
            request.LastName.Trim(),
            NationalId.Parse(request.NationalId),
            request.BirthDate,
            ct);

        return new IdentityVerifiedResponse(nationalId.Masked);
    }

    /// <summary>
    /// Sözleşme ve aydınlatma metninin onayı; telefon ve kimlik doğrulandıysa hesap
    /// <c>Unverified</c>'a geçiyor. Onaylanan sürüm gösterilen güncel sürüm olmalı.
    /// </summary>
    [HttpPost("basic-verification")]
    [ProducesResponseType<BasicVerificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<BasicVerificationResponse> BasicVerification(
        [FromBody] BasicVerificationRequest request, CancellationToken ct)
    {
        var completed = await verification.CompleteBasicAsync(
            User.Subject(), request.TermsVersion, request.PrivacyNoticeVersion, ct);

        return new BasicVerificationResponse(completed.AccountId, completed.KycLevel);
    }
}
