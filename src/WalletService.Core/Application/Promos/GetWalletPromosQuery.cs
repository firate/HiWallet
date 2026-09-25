namespace HiWallet.WalletService.Application.Promos;

/// <summary>
/// Cüzdanın promo partileri, yeniden eskiye (decisions.md madde 37). Cüzdanın
/// toplam promo bakiyesi "bu işyerinde ne kadar kullanabilirim" sorusunu
/// cevaplamıyor; bu liste her partinin kalanını, bitişini ve kapsamını dönüyor.
///
/// Sayfalama cursor ile (<c>baseline.md</c> madde 8). Parti kimliği sıralı değil,
/// sıra <c>(created_at, id)</c>; cursor son partinin kimliği.
/// </summary>
/// <param name="After">Önceki sayfanın son partisinin kimliği. İlk sayfada <c>null</c>.</param>
/// <param name="Size">İstenen sayfa boyutu. <see cref="WalletPromoPage.MaxSize"/>'a çekiliyor.</param>
public sealed record GetWalletPromosQuery(Guid WalletId, Guid? After, int Size);

/// <param name="Remaining">Tutardan harcama ve süre sonu tüketimleri düşülmüş kalan.</param>
/// <param name="MerchantAccountIds">
/// Partinin geçerli olduğu işyeri hesapları. <c>all_businesses</c> kapsamında boş.
/// </param>
/// <param name="Expired">
/// Bitiş tarihi geçti mi. Süresi dolan parti ödemeye girmiyor; kalanı süre sonu
/// işi kapatana kadar cüzdanın promo bakiyesinde görünüyor.
/// </param>
public sealed record WalletPromoView(
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

/// <param name="NextCursor">
/// Bir sonraki sayfa isteğinde <c>after</c> olarak gönderilecek değer. Son sayfada <c>null</c>.
/// </param>
public sealed record WalletPromoPage(IReadOnlyList<WalletPromoView> Items, int Size, Guid? NextCursor)
{
    public const int MaxSize = 100;

    public const int DefaultSize = 50;
}
