using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletApi.Responses;

/// <param name="PeriodStart">Ayın başı, UTC: kullanım buradan sayılıyor.</param>
/// <param name="BalanceCap">Bakiye tavanı; kimliği tespit edilmiş seviyede <c>null</c>.</param>
/// <param name="Balance">Tavana sayılan bakiye: bütün cüzdanlar ve açık kart payları.</param>
public sealed record AccountLimitsResponse(
    Guid AccountId,
    KycLevel KycLevel,
    string Currency,
    DateTimeOffset PeriodStart,
    IReadOnlyList<MovementLimitResponse> Movements,
    decimal? BalanceCap,
    decimal Balance)
{
    public static AccountLimitsResponse From(AccountLimitsView view) => new(
        view.AccountId,
        view.KycLevel,
        view.Currency,
        view.PeriodStart,
        [.. view.Movements.Select(m => new MovementLimitResponse(m.Movement, m.Limit, m.Used, m.Remaining))],
        view.BalanceCap,
        view.Balance);
}

/// <param name="Movement">
/// <c>IncomingTransfer</c>, <c>OutgoingTransfer</c>, <c>Payment</c>, <c>Withdrawal</c>,
/// <c>Deposit</c> (yükleme) ya da <c>IncomingTotal</c> (yükleme ve gelen transfer birlikte).
/// </param>
/// <param name="Limit">Ayın limiti; sıfır, hareketin bu seviyede kapalı olduğu demek.</param>
public sealed record MovementLimitResponse(KycMovement Movement, decimal Limit, decimal Used, decimal Remaining);
