using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class GetAccountLimitsHandler(
    IDbContextFactory<WalletDbContext> contextFactory, KycLimitPolicy limits, IClock clock)
{
    public async Task<AccountLimitsView> HandleAsync(GetAccountLimitsQuery query, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var level = await db.Accounts
                        .AsNoTracking()
                        .Where(a => a.Id == query.AccountId)
                        .Select(a => new { a.KycLevel })
                        .SingleOrDefaultAsync(ct)
                    ?? throw new AccountNotFoundException(query.AccountId);

        if (level.KycLevel is not { } kycLevel)
        {
            throw new KycLevelNotApplicableException(query.AccountId);
        }

        var (accountId, currency, now) = (query.AccountId, query.Currency, clock.UtcNow);

        var received = await IncomingUsage.ThisMonthAsync(db, accountId, currency, now, ct);
        var balance = await IncomingUsage.BalanceAsync(db, accountId, currency, ct);
        var sent = await OutgoingUsage.TransfersThisMonthAsync(db, accountId, KycMovement.OutgoingTransfer, currency, now, ct);
        var paid = await OutgoingUsage.TransfersThisMonthAsync(db, accountId, KycMovement.Payment, currency, now, ct);
        var withdrawn = await OutgoingUsage.WithdrawnSinceAsync(
            db, accountId, currency, OutgoingUsage.StartOfMonth(now), ct);

        var used = new Dictionary<KycMovement, decimal>
        {
            [KycMovement.IncomingTransfer] = received.Transfers.Amount,
            [KycMovement.OutgoingTransfer] = sent.Amount,
            [KycMovement.Payment] = paid.Amount,
            [KycMovement.Withdrawal] = withdrawn.Amount,
            [KycMovement.Deposit] = received.Deposits.Amount,
            [KycMovement.IncomingTotal] = (received.Deposits + received.Transfers).Amount
        };

        return new AccountLimitsView(
            accountId,
            kycLevel,
            currency.Code,
            OutgoingUsage.StartOfMonth(now),
            [.. Enum.GetValues<KycMovement>()
                .Select(movement => new MovementLimitView(movement, limits.MonthlyLimit(kycLevel, movement), used[movement]))],
            limits.BalanceCap(kycLevel),
            balance.Amount);
    }
}
