using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.Onboarding;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalMobileApi.Controllers;

/// <summary>Kayıt, kimliksiz; kovası IP'ye göre ve dar.</summary>
[ApiController]
[Route("v1/registrations")]
[EnableRateLimiting(EdgeRateLimiting.RegistrationPolicy)]
public sealed class RegistrationsController(OnboardingClient onboarding) : RegistrationsControllerBase(onboarding);

/// <summary>Temel doğrulama, müşterinin kendi kimliğiyle.</summary>
[ApiController]
[Route("v1/me")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class OnboardingController(OnboardingClient onboarding) : OnboardingControllerBase(onboarding);
