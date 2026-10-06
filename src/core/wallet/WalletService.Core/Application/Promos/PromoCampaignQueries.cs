using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Promos;

/// <param name="Granted">Kampanyanın şimdiye kadar verdiği partilerin toplamı; bütçeden düşülen.</param>
public sealed record PromoCampaignView(
    Guid CampaignId,
    string Name,
    PromoCampaignRule Rule,
    decimal? ThresholdAmount,
    PromoRewardType RewardType,
    decimal? RewardAmount,
    decimal? RewardRate,
    decimal? RewardMax,
    string Currency,
    PromoScope GrantScope,
    TimeSpan? GrantValidFor,
    decimal Budget,
    decimal DailyCapPerAccount,
    decimal TotalCapPerAccount,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyList<Guid> TriggerMerchantAccountIds,
    IReadOnlyList<Guid> ScopeMerchantAccountIds,
    decimal Granted,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    string? EndedBy)
{
    public static PromoCampaignView From(PromoCampaign campaign, decimal granted) => new(
        campaign.Id,
        campaign.Name,
        campaign.Rule,
        campaign.ThresholdAmount,
        campaign.RewardType,
        campaign.RewardAmount,
        campaign.RewardRate,
        campaign.RewardMax,
        campaign.Currency.Code,
        campaign.GrantScope,
        campaign.GrantValidFor,
        campaign.Budget,
        campaign.DailyCapPerAccount,
        campaign.TotalCapPerAccount,
        campaign.StartsAt,
        campaign.EndsAt,
        [.. campaign.TriggerMerchants],
        [.. campaign.ScopeMerchants],
        granted,
        campaign.CreatedAt,
        campaign.CreatedBy,
        campaign.EndedBy);
}

public sealed record GetPromoCampaignQuery(Guid CampaignId);

/// <param name="After">Önceki sayfanın son kampanyasının kimliği. İlk sayfada verilmiyor.</param>
public sealed record ListPromoCampaignsQuery(Guid? After, int Size);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record PromoCampaignPage(IReadOnlyList<PromoCampaignView> Items, int Size, Guid? NextCursor)
{
    public const int MaxSize = 100;

    public const int DefaultSize = 20;
}

public sealed class PromoCampaignQueryHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<PromoCampaignView> HandleAsync(GetPromoCampaignQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var campaign = await db.PromoCampaigns
                           .AsNoTracking()
                           .Include(c => c.Merchants)
                           .FirstOrDefaultAsync(c => c.Id == query.CampaignId, ct)
                       ?? throw new PromoCampaignNotFoundException(query.CampaignId);

        return PromoCampaignView.From(campaign, (await GrantedAsync(db, [campaign.Id], ct)).GetValueOrDefault(campaign.Id));
    }

    /// <summary>Yeniden eskiye. Sayfalama cursor ile: açılış zamanı ve kimlik.</summary>
    public async Task<PromoCampaignPage> HandleAsync(ListPromoCampaignsQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var size = Math.Clamp(query.Size, 1, PromoCampaignPage.MaxSize);
        var rows = db.PromoCampaigns.AsNoTracking();

        if (query.After is { } after)
        {
            var anchor = await rows
                .Where(c => c.Id == after)
                .Select(c => new { c.CreatedAt, c.Id })
                .SingleOrDefaultAsync(ct);

            if (anchor is null)
            {
                return new PromoCampaignPage([], size, null);
            }

            rows = rows.Where(c => EF.Functions.LessThan(
                ValueTuple.Create(c.CreatedAt, c.Id), ValueTuple.Create(anchor.CreatedAt, anchor.Id)));
        }

        // Bir fazla okunuyor: son sayfada mıyız sorusu COUNT sorgusu koşmadan cevaplanıyor.
        var page = await rows
            .Include(c => c.Merchants)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(size + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > size;
        var items = hasMore ? page[..size] : page;
        var granted = await GrantedAsync(db, [.. items.Select(c => c.Id)], ct);

        return new PromoCampaignPage(
            [.. items.Select(c => PromoCampaignView.From(c, granted.GetValueOrDefault(c.Id)))],
            size,
            hasMore ? items[^1].Id : null);
    }

    internal static async Task<Dictionary<Guid, decimal>> GrantedAsync(
        WalletDbContext db, IReadOnlyCollection<Guid> campaignIds, CancellationToken ct)
    {
        return await db.PromoGrants
            .Where(g => g.CampaignId != null && campaignIds.Contains(g.CampaignId.Value))
            .GroupBy(g => g.CampaignId!.Value)
            .Select(g => new { CampaignId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(g => g.CampaignId, g => g.Amount, ct);
    }
}
