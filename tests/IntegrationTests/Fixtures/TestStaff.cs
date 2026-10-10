using HiWallet.Shared.Infrastructure.Authentication;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Panelde kurulan tipik rollerin izinleri. Servisler yalnızca izne bakıyor; bu adlar
/// testleri okumak için.
/// </summary>
public static class TestStaff
{
    public static readonly string[] Support = [StaffPermissions.CustomerView, StaffPermissions.CampaignView];

    public static readonly string[] Operations = [StaffPermissions.CustomerView, StaffPermissions.WithdrawalReview];

    public static readonly string[] Finance =
        [StaffPermissions.CustomerView, StaffPermissions.DepositView, StaffPermissions.DepositResolve];

    public static readonly string[] Marketing =
    [
        StaffPermissions.CustomerView,
        StaffPermissions.CampaignView,
        StaffPermissions.CampaignManage,
        StaffPermissions.PromoGrant,
        StaffPermissions.MerchantPromoAcceptance
    ];
}
