using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Application.Deposits;

/// <summary>Askıdaki havaleler, yeniden eskiye.</summary>
/// <param name="After">Önceki sayfanın son satırı (<c>nextCursor</c>).</param>
public sealed record ListSuspendedDepositsQuery(Guid? After, int Size);

/// <param name="Id">Askı kaydının ledger işlemi.</param>
/// <param name="AccountId">Açıklamadaki numaranın hesabı, bulunduysa.</param>
public sealed record SuspendedDepositView(
    Guid Id,
    string Provider,
    string BankReference,
    Money Amount,
    DepositHoldReason Reason,
    Guid? AccountId,
    AccountNumber? AccountNumber,
    DateTimeOffset ReceivedAt,
    DateTimeOffset CreatedAt);

public sealed record SuspendedDepositPage(IReadOnlyList<SuspendedDepositView> Items, int Size, Guid? NextCursor)
{
    public const int MaxSize = 100;

    public const int DefaultSize = 20;
}
