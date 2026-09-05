using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// Seed ile oluşturulan sistem hesapları ve sabit kimlikleri.
///
/// Kimlikler SABİT: seed migration'ın idempotent olması için. Rastgele üretilseydi her
/// migration yeniden çalıştığında yeni hesap doğardı ve ledger iki farklı `clearing`
/// hesabına bölünürdü.
///
/// Kimlikler koddan da okunabilir olmalı — transfer akışı komisyonu `revenue` hesabına
/// yazarken bu hesabı bulmak zorunda. Her seferinde
/// <c>WHERE type='revenue' AND currency='TRY'</c> sorgusu koşturmak yerine kimlik burada.
///
/// Yeni bir sağlayıcı veya para birimi eklemek: buraya satır + yeni bir seed migration.
/// İkisi birlikte yapılmazsa hesap bulunamaz.
/// </summary>
public static class SystemAccounts
{
    /// <summary>Şu an desteklenen tek para birimi (decisions.md madde 6).</summary>
    public const string DefaultCurrencyCode = "TRY";

    /// <summary>Kart sağlayıcısı. Ücreti settlement anında kesiyor (FeeSettlement: Net).</summary>
    public const string StripeFake = "stripe-fake";

    /// <summary>Banka. Ücreti dönem sonu faturasıyla alıyor (FeeSettlement: Invoiced).</summary>
    public const string BankFake = "bank-fake";

    /// <summary>
    /// Seed zaman damgası. <c>now()</c> kullanılamaz — HasData sabit değer istiyor,
    /// aksi halde her migration diff'i "değişmiş" gibi görünürdü.
    /// </summary>
    public static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static Currency DefaultCurrency => Currency.From(DefaultCurrencyCode);

    /// <summary>Müşteriden alınan komisyon. Sağlayıcıya bağlı değil, currency başına tek.</summary>
    public static readonly Guid RevenueTry = new("a0000000-0000-4000-8000-000000000001");

    /// <summary>Stripe'tan alacağımız / ona borcumuz olan, henüz settle olmamış para.</summary>
    public static readonly Guid ClearingStripeTry = new("a0000000-0000-4000-8000-000000000002");

    /// <summary>Bankadan alacağımız / ona borcumuz olan, henüz settle olmamış para.</summary>
    public static readonly Guid ClearingBankTry = new("a0000000-0000-4000-8000-000000000003");

    /// <summary>
    /// Bankadaki gerçek paramız. Stripe'ın nostro'su YOK — nostro bir banka hesabı,
    /// Stripe banka değil; onun tarafındaki bakiye `clearing` ile takip ediliyor.
    /// </summary>
    public static readonly Guid NostroBankTry = new("a0000000-0000-4000-8000-000000000004");

    /// <summary>Stripe'a ödediğimiz ücret (gider).</summary>
    public static readonly Guid ProviderExpenseStripeTry = new("a0000000-0000-4000-8000-000000000005");

    /// <summary>Bankaya ödediğimiz ücret (gider).</summary>
    public static readonly Guid ProviderExpenseBankTry = new("a0000000-0000-4000-8000-000000000006");

    /// <summary>Seed edilen tüm sistem hesapları — testler ve mutabakat bunu dolaşır.</summary>
    public static IReadOnlyList<(Guid Id, LedgerAccountType Type, string? Provider)> All { get; } =
    [
        (RevenueTry, LedgerAccountType.Revenue, null),
        (ClearingStripeTry, LedgerAccountType.Clearing, StripeFake),
        (ClearingBankTry, LedgerAccountType.Clearing, BankFake),
        (NostroBankTry, LedgerAccountType.Nostro, BankFake),
        (ProviderExpenseStripeTry, LedgerAccountType.ProviderExpense, StripeFake),
        (ProviderExpenseBankTry, LedgerAccountType.ProviderExpense, BankFake)
    ];
}
