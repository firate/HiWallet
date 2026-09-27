namespace HiWallet.EdgeApi.Contracts;

/// <param name="FunderWalletId">İşyerinin cüzdanı. Promo bu cüzdanın cash kovasından çıkıyor.</param>
/// <param name="WalletId">Promo'yu alan müşterinin cüzdanı.</param>
/// <param name="ExpiresAt">Opsiyonel. Verilmezse parti süresiz.</param>
public sealed record GrantPromoRequest(
    Guid FunderWalletId,
    Guid WalletId,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

/// <param name="Replayed">
/// <c>true</c> ise bu <c>Idempotency-Key</c> daha önce işlenmişti; yeni parti açılmadı.
/// </param>
public sealed record PromoGrantResponse(Guid GrantId, bool Replayed);

public sealed record SetAcceptsPromoRequest(bool AcceptsPromo);

/// <summary>Personel promo'su: platform fonlu, kapsamı çalışanın seçtiği.</summary>
/// <param name="Scope"><c>all_businesses</c> ya da <c>selected_businesses</c>.</param>
/// <param name="MerchantAccountIds">Seçili işyerleri kapsamında işyeri hesapları.</param>
/// <param name="ExpiresAt">Opsiyonel. Verilmezse parti süresiz.</param>
public sealed record GrantStaffPromoRequest(
    decimal Amount,
    string Currency,
    string Scope,
    IReadOnlyList<Guid>? MerchantAccountIds,
    DateTimeOffset? ExpiresAt);

/// <summary>Kampanya tanımı; metin değerler ve alanların anlamı wallet-api'deki gibi.</summary>
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
    IReadOnlyList<Guid>? ScopeMerchantAccountIds);

/// <param name="Granted">Şimdiye kadar verilen partilerin toplamı; bütçeden düşülen.</param>
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
    string? EndedBy);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record PromoCampaignsResponse(IReadOnlyList<PromoCampaignResponse> Items, int Size, Guid? NextCursor);
