using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BackofficeBff.Controllers;

/// <summary>
/// Promo kampanyaları. Açmak ve bitirmek <c>campaign.manage</c>, görüntülemek
/// <c>campaign.view</c> izniyle; izni wallet-api kontrol ediyor.
/// </summary>
[ApiController]
[Route("v1/promo-campaigns")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class PromoCampaignsController(WalletApiClient walletApi) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PromoCampaignResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreatePromoCampaignRequest request, CancellationToken ct)
    {
        var response = await walletApi.PostAsync<PromoCampaignResponse>(
            "v1/promo-campaigns", request, idempotencyKey: null, ct);

        return CreatedAtAction(nameof(GetById), new { campaignId = response.CampaignId }, response);
    }

    /// <summary>Kampanyalar, yeniden eskiye. Sayfalama cursor ile.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [ProducesResponseType<PromoCampaignsResponse>(StatusCodes.Status200OK)]
    public async Task<PromoCampaignsResponse> List([FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct)
    {
        return await walletApi.GetAsync<PromoCampaignsResponse>(
            InternalServiceClient.Paged("v1/promo-campaigns", after?.ToString(), size), ct);
    }

    [HttpGet("{campaignId:guid}")]
    [ProducesResponseType<PromoCampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PromoCampaignResponse> GetById(Guid campaignId, CancellationToken ct)
    {
        return await walletApi.GetAsync<PromoCampaignResponse>($"v1/promo-campaigns/{campaignId}", ct);
    }

    /// <summary>Kampanyayı şimdi bitirir; verilmiş partiler olduğu gibi kalıyor.</summary>
    [HttpPost("{campaignId:guid}/end")]
    [ProducesResponseType<PromoCampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PromoCampaignResponse> End(Guid campaignId, CancellationToken ct)
    {
        return await walletApi.PostAsync<PromoCampaignResponse>(
            $"v1/promo-campaigns/{campaignId}/end", body: null, idempotencyKey: null, ct);
    }
}
