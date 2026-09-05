using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// Domain tipleri ↔ kolon değerleri. Enum'lar DB'de snake_case text; eşleme burada
/// AÇIKÇA yazılır, EF'in global convention'ına bırakılmaz (structure.md "Adlandırma").
/// Bir enum değeri eklendiğinde bu tablolar da güncellenmeli — aksi halde
/// çalışma anında <see cref="ArgumentOutOfRangeException"/> alınır, sessiz hata olmaz.
/// </summary>
internal static class ValueConverters
{
    /// <summary>
    /// <c>char(3)</c> ↔ <see cref="Currency"/>.
    ///
    /// DİKKAT: okurken yalnızca kod var, minor unit yok — <see cref="Currency.From(string,int)"/>
    /// varsayılanı (2) uygulanır. TRY için doğru. Minor unit'i 2 OLMAYAN bir para birimi
    /// (JPY 0, BHD 3) kullanılacaksa buraya bir eşleme eklemek ZORUNLU, yoksa tutarlar
    /// sessizce yanlış basamağa yuvarlanır.
    /// </summary>
    public static readonly ValueConverter<Currency, string> Currency =
        new(c => c.Code, code => Domain.Ledger.Currency.From(code));

    public static readonly ValueConverter<AccountType, string> AccountType =
        new(t => ToText(t), text => ToAccountType(text));

    public static readonly ValueConverter<LedgerAccountType, string> LedgerAccountType =
        new(t => ToText(t), text => ToLedgerAccountType(text));

    public static readonly ValueConverter<LedgerTransactionType, string> LedgerTransactionType =
        new(t => ToText(t), text => ToLedgerTransactionType(text));

    private static string ToText(AccountType type)
    {
        return type switch
        {
            Domain.Accounts.AccountType.Person => "person",
            Domain.Accounts.AccountType.Business => "business",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Eşlemesi yazılmamış hesap tipi.")
        };
    }

    private static AccountType ToAccountType(string text)
    {
        return text switch
        {
            "person" => Domain.Accounts.AccountType.Person,
            "business" => Domain.Accounts.AccountType.Business,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen hesap tipi.")
        };
    }

    private static string ToText(LedgerAccountType type)
    {
        return type switch
        {
            Domain.Ledger.LedgerAccountType.UserWallet => "user_wallet",
            Domain.Ledger.LedgerAccountType.Clearing => "clearing",
            Domain.Ledger.LedgerAccountType.Revenue => "revenue",
            Domain.Ledger.LedgerAccountType.Nostro => "nostro",
            Domain.Ledger.LedgerAccountType.ProviderExpense => "provider_expense",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Eşlemesi yazılmamış ledger hesap tipi.")
        };
    }

    private static LedgerAccountType ToLedgerAccountType(string text)
    {
        return text switch
        {
            "user_wallet" => Domain.Ledger.LedgerAccountType.UserWallet,
            "clearing" => Domain.Ledger.LedgerAccountType.Clearing,
            "revenue" => Domain.Ledger.LedgerAccountType.Revenue,
            "nostro" => Domain.Ledger.LedgerAccountType.Nostro,
            "provider_expense" => Domain.Ledger.LedgerAccountType.ProviderExpense,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen ledger hesap tipi.")
        };
    }

    private static string ToText(LedgerTransactionType type)
    {
        return type switch
        {
            Domain.Ledger.LedgerTransactionType.P2P => "p2p",
            Domain.Ledger.LedgerTransactionType.P2B => "p2b",
            Domain.Ledger.LedgerTransactionType.B2P => "b2p",
            Domain.Ledger.LedgerTransactionType.B2B => "b2b",
            Domain.Ledger.LedgerTransactionType.Payment => "payment",
            Domain.Ledger.LedgerTransactionType.Topup => "topup",
            Domain.Ledger.LedgerTransactionType.Withdrawal => "withdrawal",
            Domain.Ledger.LedgerTransactionType.Refund => "refund",
            Domain.Ledger.LedgerTransactionType.Settlement => "settlement",
            Domain.Ledger.LedgerTransactionType.ProviderInvoice => "provider_invoice",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Eşlemesi yazılmamış işlem tipi.")
        };
    }

    private static LedgerTransactionType ToLedgerTransactionType(string text)
    {
        return text switch
        {
            "p2p" => Domain.Ledger.LedgerTransactionType.P2P,
            "p2b" => Domain.Ledger.LedgerTransactionType.P2B,
            "b2p" => Domain.Ledger.LedgerTransactionType.B2P,
            "b2b" => Domain.Ledger.LedgerTransactionType.B2B,
            "payment" => Domain.Ledger.LedgerTransactionType.Payment,
            "topup" => Domain.Ledger.LedgerTransactionType.Topup,
            "withdrawal" => Domain.Ledger.LedgerTransactionType.Withdrawal,
            "refund" => Domain.Ledger.LedgerTransactionType.Refund,
            "settlement" => Domain.Ledger.LedgerTransactionType.Settlement,
            "provider_invoice" => Domain.Ledger.LedgerTransactionType.ProviderInvoice,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen işlem tipi.")
        };
    }
}
