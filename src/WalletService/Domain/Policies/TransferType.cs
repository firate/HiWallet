using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Policies;

/// <summary>
/// Beş transfer tipi. Hepsi aynı çekirdekten geçer; tip yalnızca policy katmanını
/// (limit + komisyon) değiştirir, çekirdeği değil (overview.md madde 4).
/// </summary>
public enum TransferType
{
    P2P = 1,
    P2B = 2,
    B2P = 3,
    B2B = 4,

    /// <summary>Ticari ödeme. p2b ile aynı yönde ama farklı komisyon/limit tarifesi.</summary>
    Payment = 5
}

public static class TransferTypeExtensions
{
    public static LedgerTransactionType ToLedgerType(this TransferType type)
    {
        return type switch
        {
            TransferType.P2P => LedgerTransactionType.P2P,
            TransferType.P2B => LedgerTransactionType.P2B,
            TransferType.B2P => LedgerTransactionType.B2P,
            TransferType.B2B => LedgerTransactionType.B2B,
            TransferType.Payment => LedgerTransactionType.Payment,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Bilinmeyen transfer tipi.")
        };
    }

    /// <summary>
    /// Tipin taraflarla tutarlı olup olmadığı. Çekirdek owner_type'a bakmaz, policy bakar
    /// (decisions.md madde 6) — bu kontrol de policy tarafında.
    /// </summary>
    public static bool Matches(this TransferType type, OwnerType sender, OwnerType receiver)
    {
        return type switch
        {
            TransferType.P2P => sender is OwnerType.Person && receiver is OwnerType.Person,
            TransferType.P2B => sender is OwnerType.Person && receiver is OwnerType.Business,
            TransferType.B2P => sender is OwnerType.Business && receiver is OwnerType.Person,
            TransferType.B2B => sender is OwnerType.Business && receiver is OwnerType.Business,

            // Ödemeyi kim yaparsa yapsın alan taraf işletmedir.
            TransferType.Payment => receiver is OwnerType.Business,

            _ => false
        };
    }
}
