using HiWallet.WalletApi.Requests;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletApi.Setup;
using HiWallet.WalletService.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

[ApiController]
[Route("v1/person-accounts")]
[Authorize(Policy = OnboardingAccess.Policy)]
public sealed class PersonAccountsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Kaydı tamamlanan kimliğin bireysel hesabı ve ilk TRY cüzdanı, <c>Unknown</c>
    /// seviyesinde. Yalnızca onboarding çağırıyor. Kimlik başına tek hesap: ilk açılış
    /// <c>201</c>, tekrarı aynı hesapla <c>200</c>.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<PersonAccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<PersonAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PersonAccountResponse>> Open(
        [FromBody] OpenPersonAccountRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<OpenPersonAccountResult>(request.ToCommand(), ct);
        var response = PersonAccountResponse.From(result);

        if (result.Replayed)
        {
            return Ok(response);
        }

        return CreatedAtAction(
            actionName: nameof(AccountsController.GetById),
            controllerName: "Accounts",
            routeValues: new { accountId = response.AccountId },
            value: response);
    }
}
