using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Promos;

/// <summary>
/// Promo partisi: tek bir yüklemenin kaydı (decisions.md madde 37). Yazıldıktan
/// sonra DEĞİŞMEZ — harcama ve süre sonu <see cref="PromoConsumption"/> ekliyor,
/// kalan tutardan tüketimler düşülerek bulunuyor.
///
/// Ledger DEĞİL: para hareketi <see cref="LedgerTransactionId"/>'deki işlemde.
/// Bu satır o hareketin kuralını taşıyor — kim fonladı, nerede geçerli, ne zaman bitiyor.
///
/// Şema: docs/ledger-schema.md "promo_grants".
/// </summary>
public sealed class PromoGrant
{
    private readonly List<PromoGrantMerchant> _merchants = [];

    private PromoGrant()
    {
        // EF Core materialization.
    }

    private PromoGrant(
        Guid id,
        Guid ledgerAccountId,
        Money amount,
        PromoFunder funder,
        Guid? funderLedgerAccountId,
        PromoScope scope,
        DateTimeOffset? expiresAt,
        Guid ledgerTransactionId,
        DateTimeOffset createdAt)
    {
        Id = id;
        LedgerAccountId = ledgerAccountId;
        Amount = amount.Amount;
        Currency = amount.Currency;
        Funder = funder;
        FunderLedgerAccountId = funderLedgerAccountId;
        Scope = scope;
        ExpiresAt = expiresAt;
        LedgerTransactionId = ledgerTransactionId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Promo'yu alan cüzdan.</summary>
    public Guid LedgerAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public PromoFunder Funder { get; private set; }

    /// <summary>
    /// Fonlayan işyerinin cüzdanı. Süre sonunda kalan buraya dönüyor. Platform
    /// fonlu partide NULL.
    /// </summary>
    public Guid? FunderLedgerAccountId { get; private set; }

    public PromoScope Scope { get; private set; }

    /// <summary>Opsiyonel. NULL ise parti süresiz.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Promo'yu cüzdana yazan ledger işlemi.</summary>
    public Guid LedgerTransactionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// <see cref="PromoScope.SelectedBusinesses"/>'ta partinin geçerli olduğu işyeri
    /// hesapları. Yükleme anında yazılıyor; kaynağı sonradan değişse de parti etkilenmiyor.
    /// </summary>
    public IReadOnlyList<PromoGrantMerchant> Merchants => _merchants;

    public Money Money => new(Amount, Currency);

    /// <summary>
    /// İşyerinin kendi müşterisine verdiği promo. Kapsam fonlayan işyerinin kendisi ve
    /// değiştirilemiyor: başka işyerlerinde geçerli olsaydı işyeri kendi parasıyla
    /// başka bir işyerinin satışını fonlardı.
    /// </summary>
    /// <param name="funderAccountId">Fonlayan cüzdanın sahibi olan işyeri hesabı.</param>
    public static PromoGrant FundedByBusiness(
        Guid id,
        Guid walletId,
        Money amount,
        Guid funderWalletId,
        Guid funderAccountId,
        DateTimeOffset? expiresAt,
        Guid ledgerTransactionId,
        DateTimeOffset createdAt)
    {
        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Promo tutarı pozitif olmalı.", nameof(amount));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Bitiş tarihi yüklemeden sonra olmalı.", nameof(expiresAt));
        }

        var grant = new PromoGrant(
            id, walletId, amount, PromoFunder.Business, funderWalletId,
            PromoScope.SelectedBusinesses, expiresAt, ledgerTransactionId, createdAt);

        grant._merchants.Add(new PromoGrantMerchant(id, funderAccountId));

        return grant;
    }
}

/// <summary>Partinin geçerli olduğu bir işyeri hesabı.</summary>
public sealed class PromoGrantMerchant
{
    private PromoGrantMerchant()
    {
        // EF Core materialization.
    }

    internal PromoGrantMerchant(Guid grantId, Guid accountId)
    {
        GrantId = grantId;
        AccountId = accountId;
    }

    public Guid GrantId { get; private set; }

    /// <summary><c>accounts.id</c> — cüzdan değil HESAP (decisions.md madde 20).</summary>
    public Guid AccountId { get; private set; }
}

public enum PromoFunder
{
    Platform = 1,

    /// <summary>İşyeri kendi <c>cash</c> kovasından fonladı.</summary>
    Business = 2
}

/// <summary>
/// Açık bir kolon: "işyeri satırı yoksa her yerde geçerli" gibi bir kural, satır
/// yazmayı unutan bir yolun kapsamı sessizce genişletmesine açık olurdu.
/// </summary>
public enum PromoScope
{
    AllBusinesses = 1,
    SelectedBusinesses = 2
}

/// <summary>
/// Kolon değerleri ve API gövdesi aynı metni kullanıyor (FundType ile aynı gerekçe).
/// </summary>
public static class PromoTexts
{
    public static string ToText(this PromoFunder funder)
    {
        return funder switch
        {
            PromoFunder.Platform => "platform",
            PromoFunder.Business => "business",
            _ => throw new ArgumentOutOfRangeException(nameof(funder), funder, "Eşlemesi yazılmamış fonlayan.")
        };
    }

    public static PromoFunder FunderFromText(string text)
    {
        return text switch
        {
            "platform" => PromoFunder.Platform,
            "business" => PromoFunder.Business,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen fonlayan.")
        };
    }

    public static string ToText(this PromoScope scope)
    {
        return scope switch
        {
            PromoScope.AllBusinesses => "all_businesses",
            PromoScope.SelectedBusinesses => "selected_businesses",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Eşlemesi yazılmamış kapsam.")
        };
    }

    public static PromoScope ScopeFromText(string text)
    {
        return text switch
        {
            "all_businesses" => PromoScope.AllBusinesses,
            "selected_businesses" => PromoScope.SelectedBusinesses,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen kapsam.")
        };
    }
}
