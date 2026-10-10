using HiWallet.Onboarding.Application;
using HiWallet.Onboarding.Domain;
using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Onboarding.Api.Controllers;

/// <summary>
/// Müşteriyi e-posta, telefon ya da kimlik numarasıyla bulmak, çalışana. Ölçüt gövdede,
/// adreste DEĞİL: adres erişim log'larına düşüyor (<see cref="HolderChecksController"/> ile
/// aynı sebep).
/// </summary>
[ApiController]
[Route("v1/customer-searches")]
[Authorize(Policy = HiWalletPolicies.CustomerView)]
public sealed class CustomerSearchesController(CustomerLookupService customers) : ControllerBase
{
    /// <summary>
    /// Ölçüte uyan hesaplar, yeniden eskiye, en çok <see cref="CustomerLookupService.MaxMatches"/>.
    /// Eşleşme yoksa boş liste.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CustomerSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<CustomerSearchResponse> Search([FromBody] CustomerSearchRequest request, CancellationToken ct)
    {
        var matches = request switch
        {
            { Email: { } email } => await customers.ByEmailAsync(email, ct),
            { Phone: { } phone } => await customers.ByPhoneAsync(PhoneNumber.Parse(phone), ct),
            _ => await customers.ByNationalIdAsync(NationalId.Parse(request.NationalId), ct)
        };

        return new CustomerSearchResponse([.. matches.Select(CustomerMatchResponse.From)]);
    }
}
