using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BackofficeBff.Controllers;

/// <summary>
/// Müşterinin kişisel bilgisi ve aranması; onboarding'den. `customer.view` izni; onboarding
/// kontrol ediyor. Kimlik numarası ve telefon maskeli geliyor.
/// </summary>
[ApiController]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class CustomersController(OnboardingClient onboarding) : ControllerBase
{
    /// <summary>
    /// Bireysel hesabın sahibi: kişisel bilgiler, onaylar ve numara değişiklikleri.
    /// Onboarding'den açılmamış hesap (işyeri) <c>404</c>.
    /// </summary>
    [HttpGet("v1/customers/by-account/{accountId:guid}")]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<CustomerProfileResponse> ByAccount(Guid accountId, CancellationToken ct)
    {
        return await onboarding.GetAsync<CustomerProfileResponse>($"v1/customers/by-account/{accountId}", ct);
    }

    /// <summary>
    /// Müşteriyi e-posta, telefon ya da kimlik numarasıyla bulur; tek ölçüt. Ölçüt gövdede:
    /// adres erişim log'larına düşüyor.
    /// </summary>
    [HttpPost("v1/customer-searches")]
    [ProducesResponseType<CustomerSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<CustomerSearchResponse> Search([FromBody] CustomerSearchRequest request, CancellationToken ct)
    {
        return await onboarding.PostAsync<CustomerSearchResponse>("v1/customer-searches", request, idempotencyKey: null, ct);
    }
}
