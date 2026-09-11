namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// İşlemi kim başlattı (<c>decisions.md</c> madde 34).
///
/// Metin karşılıkları <c>ValueConverters</c>'da; yeni bir değer eklendiğinde orası da
/// güncellenmeli.
/// </summary>
public enum ActorType
{
    /// <summary>Hesap sahibi, müşteriye dönük bir uçtan.</summary>
    Customer = 1,

    /// <summary>Şirket çalışanı, backoffice'ten.</summary>
    Employee = 2,

    /// <summary>İnsan yok: relay, zamanlanmış iş, webhook, saga komutu.</summary>
    System = 3
}
