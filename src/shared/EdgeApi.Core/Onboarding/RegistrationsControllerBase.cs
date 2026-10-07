using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.EdgeApi.Onboarding;

/// <summary>
/// Kayıt uçları, bireysel müşterinin ön API'lerinde (web BFF'i ve mobil). KİMLİKSİZ:
/// müşterinin henüz kullanıcısı yok. İstekler onboarding'e aynen gidiyor, reddi de aynen
/// dönüyor.
///
/// Soyut: ön API'ler rotayı ve rate limit kovasını kendi alt sınıflarında veriyor.
/// Somut olsaydı bu kütüphaneyi referans veren her ön API'de (işyerininkiler dahil)
/// kendiliğinden açılırdı.
/// </summary>
[AllowAnonymous]
public abstract class RegistrationsControllerBase(OnboardingClient onboarding) : ControllerBase
{
    /// <summary>Kaydı başlatır, adrese altı haneli kod gider.</summary>
    [HttpPost]
    [ProducesResponseType<RegistrationStartedResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Start([FromBody] StartRegistrationRequest request, CancellationToken ct) =>
        Accepted(await onboarding.PostAsync<RegistrationStartedResponse>("v1/registrations", request, null, ct));

    [HttpPost("{registrationId:guid}/email-verification")]
    [ProducesResponseType<EmailVerifiedResponse>(StatusCodes.Status200OK)]
    public Task<EmailVerifiedResponse> VerifyEmail(
        Guid registrationId, [FromBody] VerifyCodeRequest request, CancellationToken ct) =>
        onboarding.PostAsync<EmailVerifiedResponse>(
            $"v1/registrations/{registrationId}/email-verification", request, null, ct);

    /// <summary>
    /// Parolayı alır; kullanıcı ve hesap açılıyor. Tekrarı aynı hesabı döner. Ardından
    /// uygulama müşteriyi girişe gönderiyor.
    /// </summary>
    [HttpPost("{registrationId:guid}/completion")]
    [ProducesResponseType<RegistrationCompletedResponse>(StatusCodes.Status200OK)]
    public Task<RegistrationCompletedResponse> Complete(
        Guid registrationId, [FromBody] CompleteRegistrationRequest request, CancellationToken ct) =>
        onboarding.PostAsync<RegistrationCompletedResponse>(
            $"v1/registrations/{registrationId}/completion", request, null, ct);
}
