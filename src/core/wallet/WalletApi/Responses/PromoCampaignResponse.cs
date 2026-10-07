using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletService.Domain.Promos;

namespace HiWallet.WalletApi.Responses;

/// <param name="GrantValidForDays">Verilen partinin geçerlilik süresi, gün. Süresizse <c>null</c>.</param>
/// <param name="Granted">Şimdiye kadar verilen partilerin toplamı; bütçeden düşülen.</param>
/// <param name="CreatedBy">Açan çalışan. Backoffice öncesi açılanlarda <c>null</c>.</param>
public sealed record PromoCampaignResponse(
    Guid CampaignId,
    string Name,
    string Rule,
    decimal? ThresholdAmount,
    string RewardType,
    decimal? RewardAmount,
    decimal? RewardRate,
    decimal? RewardMax,
    string Currency,
    string GrantScope,
    double? GrantValidForDays,
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
    public static PromoCampaignResponse From(PromoCampaignView view) => new(
        view.CampaignId,
        view.Name,
        view.Rule.ToText(),
        view.ThresholdAmount,
        view.RewardType.ToText(),
        view.RewardAmount,
        view.RewardRate,
        view.RewardMax,
        view.Currency,
        view.GrantScope.ToText(),
        view.GrantValidFor?.TotalDays,
        view.Budget,
        view.DailyCapPerAccount,
        view.TotalCapPerAccount,
        view.StartsAt,
        view.EndsAt,
        view.TriggerMerchantAccountIds,
        view.ScopeMerchantAccountIds,
        view.Granted,
        view.CreatedAt,
        view.CreatedBy,
        view.EndedBy);
}

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record PromoCampaignsResponse(IReadOnlyList<PromoCampaignResponse> Items, int Size, Guid? NextCursor)
{
    public static PromoCampaignsResponse From(PromoCampaignPage page) =>
        new([.. page.Items.Select(PromoCampaignResponse.From)], page.Size, page.NextCursor);
}
