using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Hesabın bir para birimindeki varsayılan cüzdanı: hesap numarasına gelen para buraya
/// düşüyor. Hesabın o para biriminde cüzdanı varsa tam bir varsayılanı var. İlk cüzdan
/// kendiliğinden varsayılan; müşteri onu yalnızca kendi cüzdanları arasında değiştiriyor.
///
/// Hesap ve para birimi cüzdanın kendisinden okunuyor: varsayılan başka bir hesabın ya da
/// başka bir para biriminin cüzdanını gösteremiyor.
/// </summary>
public sealed class DefaultWallet
{
    private DefaultWallet()
    {
        // EF Core materialization.
    }

    public Guid AccountId { get; private set; }

    public Currency Currency { get; private set; }

    public Guid WalletId { get; private set; }

    public static DefaultWallet Of(LedgerAccount wallet)
    {
        if (wallet is not { Type: LedgerAccountType.UserWallet, AccountId: { } accountId })
        {
            throw new ArgumentException("Varsayılan yalnızca müşterinin cüzdanı olabilir.", nameof(wallet));
        }

        return new DefaultWallet { AccountId = accountId, Currency = wallet.Currency, WalletId = wallet.Id };
    }
}
