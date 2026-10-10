using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Application.Deposits;

/// <summary>Askıdaki havaleler, yeniden eskiye.</summary>
/// <param name="After">Önceki sayfanın son satırı (<c>nextCursor</c>).</param>
public sealed record ListSuspendedDepositsQuery(Guid? After, int Size);

/// <param name="Id">Askı kaydının ledger işlemi.</param>
/// <param name="AccountId">Açıklamadaki numaranın hesabı, bulunduysa.</param>
/// <param name="Status"><c>open</c>: karara açık; <c>returning</c>: iadesi sürüyor.</param>
public sealed record SuspendedDepositView(
    Guid Id,
    string Provider,
    string BankReference,
    Money Amount,
    DepositHoldReason Reason,
    Guid? AccountId,
    AccountNumber? AccountNumber,
    DateTimeOffset ReceivedAt,
    DateTimeOffset CreatedAt,
    string Status);

public sealed record SuspendedDepositPage(IReadOnlyList<SuspendedDepositView> Items, int Size, Guid? NextCursor)
{
    public const int MaxSize = 100;

    public const int DefaultSize = 20;
}

/// <summary>Listedeki havalenin hali.</summary>
public static class SuspendedDepositStatuses
{
    /// <summary>Karara açık: aktarılabilir ya da iade edilebilir.</summary>
    public const string Open = "open";

    /// <summary>İadesi sürüyor; banka sonucu bekleniyor.</summary>
    public const string Returning = "returning";
}
