namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// Ledger hesabının rolü. Müşteri hesabının tipiyle
/// (<see cref="Accounts.AccountType"/>: person/business) karıştırılmaz — o sahibin kim
/// olduğunu, bu hesabın ledger'da ne işe yaradığını söyler. Dik boyutlar (decisions.md madde 6).
///
/// DB'de snake_case text; eşleme
/// <c>Infrastructure/Persistence/Configurations/LedgerAccountConfiguration.cs</c>'de
/// açıkça yazılır.
/// </summary>
public enum LedgerAccountType
{
    /// <summary>Müşteri cüzdanı — bir hesaba bağlı tek tip. Negatife düşemez.</summary>
    UserWallet = 1,

    /// <summary>Yolda olan / settle olmamış para. Sağlayıcı bazında.</summary>
    Clearing = 2,

    /// <summary>Müşteriden alınan komisyon (gelir).</summary>
    Revenue = 3,

    /// <summary>Kendi banka hesabımızdaki gerçek para. Banka bazında.</summary>
    Nostro = 4,

    /// <summary>Sağlayıcıya ödenen ücret (gider). Sağlayıcı bazında.</summary>
    ProviderExpense = 5,

    /// <summary>
    /// Platform fonlu promo'nun gideri (decisions.md madde 37). Promo verildiği anda
    /// yazılıyor. Sağlayıcıya bağlı değil, currency başına tek.
    /// </summary>
    PromoExpense = 6,

    /// <summary>
    /// Süresi dolan platform fonlu promo'nun kullanılmayan kalanı (gelir). Gider hesabına
    /// geri yazılmıyor, netleştirilmiyor (decisions.md madde 37).
    /// </summary>
    PromoBreakage = 7,

    /// <summary>
    /// Askı: bankaya gelmiş ama cüzdana geçirilemeyen para (sahibi belirlenemedi, limit,
    /// başkasının hesabından gönderilmiş). Banka bazında; para o bankanın nostro'sunda
    /// duruyor. Kaynağına iade edilene ya da bir cüzdana geçirilene kadar burada.
    /// </summary>
    Suspense = 8
}
