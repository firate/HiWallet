using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Bireysel hesabın seviyesi, aylık limitleri ve bu ay kullanılanı. Kullanım limit
/// kontrolünün saydığıyla aynı (<see cref="IncomingUsage"/>, <see cref="OutgoingUsage"/>):
/// müşteri gördüğünden farklı bir sayıyla reddedilmesin. İşyeri hesabının seviyesi yok.
/// </summary>
public sealed record GetAccountLimitsQuery(Guid AccountId, Currency Currency);

/// <param name="PeriodStart">Ayın başı, UTC: kullanım buradan sayılıyor.</param>
/// <param name="BalanceCap">Bakiye tavanı; kimliği tespit edilmiş seviyede <c>null</c>.</param>
/// <param name="Balance">Tavana sayılan bakiye: bütün cüzdanlar ve açık kart payları.</param>
public sealed record AccountLimitsView(
    Guid AccountId,
    KycLevel KycLevel,
    string Currency,
    DateTimeOffset PeriodStart,
    IReadOnlyList<MovementLimitView> Movements,
    decimal? BalanceCap,
    decimal Balance);

/// <param name="Limit">Ayın limiti; sıfır, hareketin bu seviyede kapalı olduğu demek.</param>
/// <param name="Used">
/// Bu ay kullanılan. Giden harekette cüzdandan düşen, komisyon dahil; çekimde iade
/// edilenler düşülmüş.
/// </param>
public sealed record MovementLimitView(KycMovement Movement, decimal Limit, decimal Used)
{
    public decimal Remaining => Math.Max(0m, Limit - Used);
}
