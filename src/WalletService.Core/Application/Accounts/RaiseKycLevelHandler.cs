using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class RaiseKycLevelHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task<KycLevelResult> HandleAsync(RaiseKycLevelCommand command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Satır kilitleniyor: iki yükseltme aynı anda gelirse ikincisi birincinin
        // sonucunu okuyor. Kilitsiz ikisi de eski seviyeyi okur ve son yazan kazanırdı;
        // geç gelen alt seviye üst seviyeyi geri alırdı.
        var account = await db.Accounts
                          .FromSql($"SELECT * FROM accounts WHERE id = {command.AccountId} FOR UPDATE")
                          .SingleOrDefaultAsync(ct)
                      ?? throw new AccountNotFoundException(command.AccountId);

        var changed = account.RaiseKycLevel(command.Level);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new KycLevelResult(account.Id, account.KycLevel!.Value, changed);
    }
}
