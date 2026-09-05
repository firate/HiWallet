using FluentValidation;
using HiWallet.BankService.Api.Requests;
using HiWallet.BankService.Application;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.BankService.Api.Controllers;

/// <summary>
/// Senaryo tetikleyicileri (overview.md madde 9). Gerçek bir bankada bu uç YOKTUR —
/// varlık sebebi "banka reddetti" durumunun test edilebilir olması.
///
/// Bu servis ÜRETİMDE ÇALIŞMAZ; gerçek entegrasyon geldiğinde tamamen değiştirilir.
/// O yüzden burada kimlik doğrulama yok: sahte bankanın senaryo ucunu korumak,
/// korunacak bir şey olmadığı için tören olurdu.
/// </summary>
[ApiController]
[Route("v1/scenarios")]
public sealed class ScenariosController(
    ScenarioStore scenarios, IValidator<ArmScenarioRequest> validator) : ControllerBase
{
    /// <summary>Bir saga için sonraki transferin nasıl sonuçlanacağını belirler.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Arm(
        [FromBody] ArmScenarioRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        await scenarios.ArmAsync(
            request.SagaId, request.Outcome, request.TransientFailures, request.DelayMilliseconds, ct);

        return NoContent();
    }

    /// <summary>
    /// Senaryonun o anki hali. Testler "kaç denemede sonuçlandı" sorusunu buradan
    /// cevaplıyor — geçici hata senaryosunda retry'ın gerçekten çalıştığının kanıtı.
    /// </summary>
    [HttpGet("{sagaId:guid}")]
    [ProducesResponseType<ScenarioState>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioState>> Get(Guid sagaId, CancellationToken ct)
    {
        var state = await scenarios.FindAsync(sagaId, ct);

        return state is null ? NotFound() : state;
    }
}
