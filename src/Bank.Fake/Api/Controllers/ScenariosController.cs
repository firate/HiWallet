using FluentValidation;
using HiWallet.Bank.Fake.Api.Requests;
using HiWallet.Bank.Fake.Application;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Bank.Fake.Api.Controllers;

/// <summary>
/// Senaryo tetikleyicileri (overview.md madde 9). <b>Gerçek bir bankada bu uç
/// YOKTUR</b> — varlık sebebi "banka reddetti" durumunun test edilebilir olması.
///
/// Sahte servisin tek özel yeteneği bu. Telafi yolunun çalıştığını kanıtlamanın
/// başka yolu yok: gerçek bir bankaya "şimdi reddet" diyemezsin.
/// </summary>
[ApiController]
[Route("v1/scenarios")]
public sealed class ScenariosController(
    ScenarioStore scenarios, IValidator<ScenarioRequest> validator) : ControllerBase
{
    /// <summary>Bir çekim için sonraki transferin nasıl sonuçlanacağını belirler.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Set(
        [FromBody] ScenarioRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        await scenarios.SetAsync(
            request.ClientReference, request.Outcome,
            request.TransientFailures, request.DelayMilliseconds, ct);

        return NoContent();
    }

    /// <summary>
    /// Senaryonun o anki hali. Testler "kaç denemede sonuçlandı" sorusunu buradan
    /// cevaplıyor — geçici hata senaryosunda retry'ın gerçekten çalıştığının kanıtı.
    /// </summary>
    [HttpGet("{clientReference}")]
    [ProducesResponseType<ScenarioState>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioState>> Get(string clientReference, CancellationToken ct)
    {
        var state = await scenarios.FindAsync(clientReference, ct);

        return state is null ? NotFound() : state;
    }
}
