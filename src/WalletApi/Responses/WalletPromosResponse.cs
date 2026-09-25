using HiWallet.WalletService.Application.Promos;

namespace HiWallet.WalletApi.Responses;

/// <param name="Remaining">Tutardan harcama ve süre sonu tüketimleri düşülmüş kalan.</param>
/// <param name="Funder"><c>business</c> ya da <c>platform</c>.</param>
/// <param name="Scope"><c>selected_businesses</c> ya da <c>all_businesses</c>.</param>
/// <param name="Expired">
/// Bitiş tarihi geçti: parti ödemeye girmiyor, kalanı kapatılana kadar bakiyede görünüyor.
/// </param>
public sealed record WalletPromoResponse(
    Guid GrantId,
    decimal Amount,
    decimal Remaining,
    string Currency,
    string Funder,
    string Scope,
    IReadOnlyList<Guid> MerchantAccountIds,
    DateTimeOffset? ExpiresAt,
    bool Expired,
    DateTimeOffset CreatedAt);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record WalletPromosResponse(
    IReadOnlyList<WalletPromoResponse> Items,
    int Size,
    Guid? NextCursor)
{
    public static WalletPromosResponse From(WalletPromoPage page)
    {
        return new WalletPromosResponse(
            [.. page.Items.Select(p => new WalletPromoResponse(
                p.GrantId, p.Amount, p.Remaining, p.Currency, p.Funder, p.Scope,
                p.MerchantAccountIds, p.ExpiresAt, p.Expired, p.CreatedAt))],
            page.Size,
            page.NextCursor);
    }
}
