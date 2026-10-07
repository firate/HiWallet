namespace HiWallet.WalletService.Application.Promos;

/// <summary>
/// Personel promo'sunun tek seferlik tavanı, para birimi başına. Tanımlı olmayan para
/// biriminde personel promo'su verilmiyor; büyük tutar kampanyayla veriliyor.
/// </summary>
public sealed class StaffPromoOptions
{
    public const string SectionName = "Promos:StaffGrant";

    /// <summary>Anahtar ISO 4217 kodu (<c>TRY</c>), değer tek seferde verilebilecek en fazla tutar.</summary>
    public Dictionary<string, decimal> MaxAmount { get; set; } = [];
}
