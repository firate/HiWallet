using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletService.Application.Promos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

/// <summary>
/// Promo kampanyaları (decisions.md madde 37). Açmak ve bitirmek pazarlama rolünün,
/// görüntülemek her çalışanın. Müşteriye kapalı.
/// </summary>
[ApiController]
[Route("v1/promo-campaigns")]
public sealed class PromoCampaignsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Kampanya açar. Kuralın parçaları birbirini tutmuyorsa <c>400</c>; işyeri olmayan
    /// hesap ya da promo gider hesabı olmayan para birimi <c>422</c>.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = HiWalletPolicies.Marketing)]
    [ProducesResponseType<PromoCampaignResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PromoCampaignResponse>> Create(
        [FromBody] CreatePromoCampaignRequest request, CancellationToken ct)
    {
        var view = await bus.InvokeAsync<PromoCampaignView>(request.ToCommand(User.Subject()), ct);

        return CreatedAtAction(nameof(GetById), new { campaignId = view.CampaignId }, PromoCampaignResponse.From(view));
    }

    /// <summary>Kampanyalar, yeniden eskiye. Sayfalama cursor ile.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [Authorize(Policy = HiWalletPolicies.Staff)]
    [ProducesResponseType<PromoCampaignsResponse>(StatusCodes.Status200OK)]
    public async Task<PromoCampaignsResponse> List(
        CancellationToken ct,
        [FromQuery] Guid? after = null,
        [FromQuery] int size = PromoCampaignPage.DefaultSize)
    {
        var page = await bus.InvokeAsync<PromoCampaignPage>(new ListPromoCampaignsQuery(after, size), ct);

        return PromoCampaignsResponse.From(page);
    }

    [HttpGet("{campaignId:guid}")]
    [Authorize(Policy = HiWalletPolicies.Staff)]
    [ProducesResponseType<PromoCampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PromoCampaignResponse> GetById(Guid campaignId, CancellationToken ct)
    {
        return PromoCampaignResponse.From(
            await bus.InvokeAsync<PromoCampaignView>(new GetPromoCampaignQuery(campaignId), ct));
    }

    /// <summary>
    /// Kampanyayı şimdi bitirir: bundan sonraki ödemeler değerlendirilmiyor, verilmiş
    /// partiler olduğu gibi kalıyor. Bitmiş kampanyada bir şey değişmiyor.
    /// </summary>
    [HttpPost("{campaignId:guid}/end")]
    [Authorize(Policy = HiWalletPolicies.Marketing)]
    [ProducesResponseType<PromoCampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PromoCampaignResponse> End(Guid campaignId, CancellationToken ct)
    {
        return PromoCampaignResponse.From(
            await bus.InvokeAsync<PromoCampaignView>(new EndPromoCampaignCommand(campaignId, User.Subject()), ct));
    }
}
