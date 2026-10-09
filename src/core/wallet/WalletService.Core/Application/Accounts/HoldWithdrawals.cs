using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Hesabın bankaya çekimini bir süre kapatır. Telefon numarası değişince onboarding
/// koyuyor: hesabı ele geçiren numarayı değiştirip parayı hemen çekemesin.
/// </summary>
public sealed record HoldWithdrawalsCommand(Guid AccountId, DateTimeOffset Until);

/// <param name="Until">Geçerli bekletmenin sonu; istenenden geç olabilir, kısalmıyor.</param>
public sealed record WithdrawalHoldResult(Guid AccountId, DateTimeOffset Until);

public sealed class HoldWithdrawalsHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<WithdrawalHoldResult> HandleAsync(HoldWithdrawalsCommand command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Satır kilitleniyor: aynı anda gelen iki bekletmeden kısa olan uzunu ezmesin.
        var account = await db.Accounts
                          .FromSql($"SELECT * FROM accounts WHERE id = {command.AccountId} FOR UPDATE")
                          .SingleOrDefaultAsync(ct)
                      ?? throw new AccountNotFoundException(command.AccountId);

        var until = account.HoldWithdrawalsUntil(command.Until);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new WithdrawalHoldResult(account.Id, until);
    }
}
