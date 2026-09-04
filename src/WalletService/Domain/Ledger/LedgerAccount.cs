namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// Bakiye tutabilen her şey: müşteri cüzdanları ve sistem hesapları aynı tabloda.
///
/// Ayrı tablolara bölünemiyorlar çünkü <see cref="LedgerEntry"/> tek bir FK hedefine
/// işaret etmek zorunda — bir transfer'in bacakları hem cüzdan hem <c>revenue</c>
/// olabiliyor. İkisi ayrı tablolarda olsaydı polimorfik FK gerekirdi ve referential
/// integrity çökerdi.
///
/// Cüzdan = <see cref="Type"/> değeri <see cref="LedgerAccountType.UserWallet"/> olan
/// satır; <see cref="AccountId"/>'si dolu olan tek tip odur.
///
/// Şema: docs/ledger-schema.md "ledger_accounts".
/// </summary>
public sealed class LedgerAccount
{
    private LedgerAccount()
    {
        // EF Core materialization.
    }

    private LedgerAccount(
        Guid id,
        LedgerAccountType type,
        Guid? accountId,
        string? name,
        string? provider,
        Currency currency,
        DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        AccountId = accountId;
        Name = name;
        Provider = provider;
        Currency = currency;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public LedgerAccountType Type { get; private set; }

    /// <summary>
    /// Sahibi olan müşteri hesabı. Cüzdanlarda zorunlu, sistem hesaplarında NULL.
    /// Limitler bu kimlik üzerinden toplanır — cüzdan bazında değil (decisions.md madde 20).
    /// </summary>
    public Guid? AccountId { get; private set; }

    /// <summary>
    /// Cüzdan adı ("Birikim"). Cüzdanlarda zorunlu, sistem hesaplarında NULL.
    /// Aynı hesabın aynı para birimindeki cüzdanları başka türlü ayırt edilemiyor.
    /// </summary>
    public string? Name { get; private set; }

    /// <summary>
    /// Sistem hesabının hangi dış tarafa ait olduğu (decisions.md madde 14).
    /// Cüzdanlarda NULL. <see cref="LedgerAccountType.Revenue"/> kendi paramızın hesabı
    /// olduğu için onda da NULL.
    /// </summary>
    public string? Provider { get; private set; }

    public Currency Currency { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Sistem hesapları negatife düşebilir, cüzdanlar düşemez.
    /// CHECK constraint DEĞİL, uygulama kuralı — clearing tasarımı gereği negatif durur
    /// (decisions.md madde 6).
    /// </summary>
    public bool CanGoNegative => Type is not LedgerAccountType.UserWallet;

    public static LedgerAccount Wallet(
        Guid id,
        Guid accountId,
        string name,
        Currency currency,
        DateTimeOffset createdAt)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Cüzdan bir hesaba bağlı olmalı.", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Cüzdanın adı olmalı — aynı hesabın aynı para birimindeki cüzdanları " +
                "başka türlü ayırt edilemiyor (decisions.md madde 20).", nameof(name));
        }

        return new LedgerAccount(
            id, LedgerAccountType.UserWallet, accountId, name, provider: null, currency, createdAt);
    }

    /// <summary>
    /// Sistem hesabı. <paramref name="provider"/> yalnızca
    /// <see cref="LedgerAccountType.Revenue"/>'da NULL olabilir — diğerlerinde mutabakat
    /// sağlayıcı bazında koştuğu için zorunlu (decisions.md madde 14).
    /// </summary>
    public static LedgerAccount System(
        Guid id,
        LedgerAccountType type,
        string? provider,
        Currency currency,
        DateTimeOffset createdAt)
    {
        if (type is LedgerAccountType.UserWallet)
        {
            throw new ArgumentException(
                $"{nameof(LedgerAccountType.UserWallet)} sistem hesabı değil; {nameof(Wallet)} kullan.",
                nameof(type));
        }

        if (type is LedgerAccountType.Revenue)
        {
            if (provider is not null)
            {
                throw new ArgumentException(
                    "revenue kendi gelirimiz, sağlayıcıya bağlı değil.", nameof(provider));
            }
        }
        else if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException(
                $"{type} hesabı sağlayıcı bazında açılır, provider zorunlu.", nameof(provider));
        }

        return new LedgerAccount(
            id, type, accountId: null, name: null, provider, currency, createdAt);
    }
}
