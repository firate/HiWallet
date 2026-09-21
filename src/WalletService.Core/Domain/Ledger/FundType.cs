namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// Paranın kaynağı (decisions.md madde 36). Bakiye bu tipe göre bölünüyor: cüzdan tek
/// satır, bakiyesi kova başına bir satır.
///
/// Ayrımın sebebi mevzuat. Kredi kartı ile yüklenen fon ancak aynı kredi kartı hesabına
/// iade edilebiliyor, IBAN'a çıkamıyor; karşılığında fon yatırılmamış hediye bakiyenin
/// nakde çevrilmesi ise zaten söz konusu değil.
/// </summary>
public enum FundType
{
    /// <summary>Havale/EFT ile yüklenen para. IBAN'a çekilebilen tek kova.</summary>
    Cash = 1,

    /// <summary>Kart ile yüklenen para. IBAN'a çıkamıyor, transfer edilebiliyor.</summary>
    Card = 2,

    /// <summary>Cashback ve hediye bakiye. Nakde çevrilemiyor, transfer de edilemiyor.</summary>
    Promo = 3
}

/// <summary>
/// <see cref="FundType"/> üzerindeki kurallar. Kurallar burada duruyor ki harcama
/// sırası, çekim kısıtı ve transfer kısıtı tek kaynaktan okunsun.
/// </summary>
public static class FundTypes
{
    /// <summary>
    /// Harcama sırası: en kısıtlı kova önce eriyor (decisions.md madde 36). Ters
    /// sırada müşteri nakdini harcayıp çekilemeyen bakiyeyle kalırdı.
    ///
    /// Konfigüre EDİLMEZ: kural mevzuattan geliyor, tarife ayarı değil.
    /// </summary>
    public static readonly IReadOnlyList<FundType> SpendOrder = [FundType.Promo, FundType.Card, FundType.Cash];

    /// <summary>Kovanın tamamı, ledger'daki sırayla. Bakiye kırılımı bu sırayla dönüyor.</summary>
    public static readonly IReadOnlyList<FundType> All = [FundType.Cash, FundType.Card, FundType.Promo];

    /// <summary>
    /// IBAN'a çekilebilir mi. Yalnızca <see cref="FundType.Cash"/>.
    /// <see cref="FundType.Card"/> için yönetmelik aynı karta iadeye izin veriyor ama
    /// o yol bu sistemde kurulmadı — kapsam kararı, madde 36.
    /// </summary>
    public static bool CanWithdraw(this FundType type) => type is FundType.Cash;

    /// <summary>
    /// Başka bir cüzdana gönderilebilir mi. Gönderilebiliyorsa tip karşı tarafta
    /// AYNEN korunuyor; korunmasaydı kart kısıtı tek adımda delinirdi.
    /// </summary>
    public static bool CanTransfer(this FundType type) => type is not FundType.Promo;

    /// <summary>
    /// Kolon değeri ve API gövdesi aynı metni kullanıyor. İki yerde ayrı yazılsalardı
    /// biri değiştiğinde diğeri sessizce eski değerle kalırdı.
    /// </summary>
    public static string ToText(this FundType type)
    {
        return type switch
        {
            FundType.Cash => "cash",
            FundType.Card => "card",
            FundType.Promo => "promo",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Eşlemesi yazılmamış kaynak tipi.")
        };
    }

    public static FundType FromText(string text)
    {
        return text switch
        {
            "cash" => FundType.Cash,
            "card" => FundType.Card,
            "promo" => FundType.Promo,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen kaynak tipi.")
        };
    }
}
