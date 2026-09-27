using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletService.Domain.Promos;

namespace HiWallet.WalletApi.Requests;

/// <summary>
/// Kampanya tanımı (decisions.md madde 37). Metin değerler veritabanındaki gibi:
/// kural <c>payment_to_merchant</c> ya da <c>daily_payment_total</c>, ödül <c>fixed</c>
/// ya da <c>percentage</c>, kapsam <c>all_businesses</c> ya da <c>selected_businesses</c>.
/// </summary>
/// <param name="GrantValidForDays">Verilen partinin geçerlilik süresi, gün. Verilmezse parti süresiz.</param>
/// <param name="TriggerMerchantAccountIds">İşyerine ödeme kuralında ödemesi tetikleyen işyerleri.</param>
/// <param name="ScopeMerchantAccountIds">Seçili işyerleri kapsamında verilen partinin geçtiği işyerleri.</param>
public sealed record CreatePromoCampaignRequest(
    string Name,
    string Rule,
    decimal? ThresholdAmount,
    string RewardType,
    decimal? RewardAmount,
    decimal? RewardRate,
    decimal? RewardMax,
    string Currency,
    string GrantScope,
    int? GrantValidForDays,
    decimal Budget,
    decimal DailyCapPerAccount,
    decimal TotalCapPerAccount,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyList<Guid>? TriggerMerchantAccountIds,
    IReadOnlyList<Guid>? ScopeMerchantAccountIds)
{
    public CreatePromoCampaignCommand ToCommand(string employeeSubject) => new(
        Name,
        PromoTexts.RuleFromText(Rule),
        ThresholdAmount,
        PromoTexts.RewardTypeFromText(RewardType),
        RewardAmount,
        RewardRate,
        RewardMax,
        Currency,
        PromoTexts.ScopeFromText(GrantScope),
        GrantValidForDays is { } days ? TimeSpan.FromDays(days) : null,
        Budget,
        DailyCapPerAccount,
        TotalCapPerAccount,
        StartsAt,
        EndsAt,
        TriggerMerchantAccountIds ?? [],
        ScopeMerchantAccountIds ?? [],
        employeeSubject);
}
