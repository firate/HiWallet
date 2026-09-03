using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Ledger hesabı. Müşteri cüzdanı ya da sistem hesabı.
/// Şema: docs/ledger-schema.md "accounts".
/// </summary>
public sealed class Account
{
    private Account()
    {
        // EF Core materialization.
    }

    private Account(
        Guid id,
        AccountType type,
        Guid? ownerId,
        OwnerType? ownerType,
        string? name,
        string? provider,
        Currency currency,
        DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        OwnerId = ownerId;
        OwnerType = ownerType;
        Name = name;
        Provider = provider;
        Currency = currency;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public AccountType Type { get; private set; }

    /// <summary>
    /// Cüzdanlarda zorunlu, sistem hesaplarında NULL. Bir sahibin aynı para biriminde
    /// birden fazla cüzdanı olabilir (decisions.md madde 20) — bu yüzden limitler
    /// cüzdan değil SAHİP bazında uygulanır.
    /// </summary>
    public Guid? OwnerId { get; private set; }

    /// <summary>
    /// Cüzdanlarda zorunlu, sistem hesaplarında NULL. Aynı sahibin cüzdanları arasında
    /// sapamaz — DB'de <c>(owner_id, owner_type)</c> composite FK ile <c>owners</c>'a bağlı.
    /// </summary>
    public OwnerType? OwnerType { get; private set; }

    /// <summary>
    /// Cüzdan adı ("Birikim"). Cüzdanlarda zorunlu, sistem hesaplarında NULL.
    /// Aynı sahibin aynı para birimindeki cüzdanları başka türlü ayırt edilemiyor.
    /// </summary>
    public string? Name { get; private set; }

    /// <summary>
    /// Sistem hesabının hangi dış tarafa ait olduğu (decisions.md madde 14).
    /// Cüzdanlarda NULL. <see cref="AccountType.Revenue"/> kendi paramızın hesabı
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
    public bool CanGoNegative => Type is not AccountType.UserWallet;

    public static Account UserWallet(
        Guid id,
        Guid ownerId,
        OwnerType ownerType,
        string name,
        Currency currency,
        DateTimeOffset createdAt)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("Cüzdanın sahibi olmalı.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Cüzdanın adı olmalı — aynı sahibin aynı para birimindeki cüzdanları " +
                "başka türlü ayırt edilemiyor (decisions.md madde 20).", nameof(name));
        }

        return new Account(id, AccountType.UserWallet, ownerId, ownerType, name, provider: null, currency, createdAt);
    }

    /// <summary>
    /// Sistem hesabı. <paramref name="provider"/> yalnızca
    /// <see cref="AccountType.Revenue"/>'da NULL olabilir — diğerlerinde mutabakat
    /// sağlayıcı bazında koştuğu için zorunlu (decisions.md madde 14).
    /// </summary>
    public static Account System(
        Guid id,
        AccountType type,
        string? provider,
        Currency currency,
        DateTimeOffset createdAt)
    {
        if (type is AccountType.UserWallet)
        {
            throw new ArgumentException(
                $"{nameof(AccountType.UserWallet)} sistem hesabı değil; {nameof(UserWallet)} kullan.",
                nameof(type));
        }

        if (type is AccountType.Revenue)
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

        return new Account(
            id, type, ownerId: null, ownerType: null, name: null, provider, currency, createdAt);
    }
}
