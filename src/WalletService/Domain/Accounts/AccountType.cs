namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
///     Hesabın ledger'daki rolü. <see cref="OwnerType" /> ile dik boyut — birleştirilmez
///     (decisions.md madde 6). DB'de snake_case text; eşleme
///     <c>Infrastructure/Persistence/Configurations/AccountConfiguration.cs</c>'de açıkça yazılır.
/// </summary>
public enum AccountType
{
    /// <summary>Müşteri cüzdanı. Negatife düşemez.</summary>
    UserWallet = 1,

    /// <summary>Yolda olan / settle olmamış para. Sağlayıcı bazında.</summary>
    Clearing = 2,

    /// <summary>Müşteriden alınan komisyon (gelir).</summary>
    Revenue = 3,

    /// <summary>Kendi banka hesabımızdaki gerçek para. Banka bazında.</summary>
    Nostro = 4,

    /// <summary>Sağlayıcıya ödenen ücret (gider). Sağlayıcı bazında.</summary>
    ProviderExpense = 5
}