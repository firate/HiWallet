using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class OpenAccountHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock)
{
    public async Task<OpenAccountResult> HandleAsync(OpenAccountCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var account = Account.Open(Guid.NewGuid(), command.Type, now);

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        return new OpenAccountResult(account.Id, account.Type, account.CreatedAt);
    }
}
