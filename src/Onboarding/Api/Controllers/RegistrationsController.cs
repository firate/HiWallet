using HiWallet.Onboarding.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Onboarding.Api.Controllers;

/// <summary>
/// Kayıt uçları KİMLİKSİZ: müşterinin henüz kullanıcısı yok. Ön API'ler çağırıyor ve
/// müşteri başına sınırı (IP'ye göre) orada uyguluyor; bu servis iç ağda.
/// </summary>
[ApiController]
[Route("v1/registrations")]
[AllowAnonymous]
public sealed class RegistrationsController(RegistrationService registrations) : ControllerBase
{
    /// <summary>
    /// Kaydı başlatır ve adrese altı haneli kod gönderir. Adres kayıtlı olsa da cevap
    /// aynı: burada söylemek başkasının e-postasının müşteri olup olmadığını verirdi.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<RegistrationStartedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Start([FromBody] StartRegistrationRequest request, CancellationToken ct)
    {
        var started = await registrations.StartAsync(request.Email, ct);

        return Accepted(new RegistrationStartedResponse(started.RegistrationId, started.CodeExpiresAt));
    }

    /// <summary>E-postaya giden kodu doğrular. Beş yanlış denemede kayıt kilitleniyor.</summary>
    [HttpPost("{registrationId:guid}/email-verification")]
    [ProducesResponseType<EmailVerifiedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<EmailVerifiedResponse> VerifyEmail(
        Guid registrationId, [FromBody] VerifyCodeRequest request, CancellationToken ct)
    {
        await registrations.VerifyEmailAsync(registrationId, request.Code, ct);

        return new EmailVerifiedResponse(EmailVerified: true);
    }

    /// <summary>
    /// Parolayı alır, kimlik sağlayıcıda kullanıcıyı ve wallet'ta hesabı açar. İlk
    /// tamamlama <c>201</c>, tekrarı aynı hesapla <c>200</c>. Bu e-postayla hesap varsa
    /// <c>409</c>: adresin sahibi olduğu artık kanıtlı.
    /// </summary>
    [HttpPost("{registrationId:guid}/completion")]
    [ProducesResponseType<RegistrationCompletedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<RegistrationCompletedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Complete(
        Guid registrationId, [FromBody] CompleteRegistrationRequest request, CancellationToken ct)
    {
        var completed = await registrations.CompleteAsync(registrationId, request.Password, ct);
        var response = new RegistrationCompletedResponse(completed.AccountId, completed.Email);

        return completed.Replayed ? Ok(response) : StatusCode(StatusCodes.Status201Created, response);
    }
}
