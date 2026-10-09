using HiWallet.Onboarding.Application;
using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Onboarding.Api.Controllers;

/// <summary>
/// Hesabın sahibi, çalışana. Müşteri kendi bilgisini <c>/v1/me</c>'den görüyor; bu uç
/// yalnızca <c>customer.view</c> izni olan çalışana açık.
/// </summary>
[ApiController]
[Route("v1/customers")]
[Authorize(Policy = HiWalletPolicies.CustomerView)]
public sealed class CustomersController(CustomerLookupService customers) : ControllerBase
{
    /// <summary>
    /// Bireysel hesabın sahibinin kişisel bilgileri, onayları ve numara değişiklikleri.
    /// Onboarding'den açılmamış hesap (işyeri) ya da olmayan hesap <c>404</c>.
    /// </summary>
    [HttpGet("by-account/{accountId:guid}")]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<CustomerProfileResponse> ByAccount(Guid accountId, CancellationToken ct) =>
        CustomerProfileResponse.From(await customers.ByAccountAsync(accountId, ct));
}
