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
        // Bireysel hesabı kayıt açıyor (OpenPersonAccountCommand); buradan açılsaydı
        // kayıt ve doğrulama atlanırdı. Sınırdaki validator bunu zaten reddediyor.
        if (command.Type is not AccountType.Business)
        {
            throw new ArgumentException("Bireysel hesap kayıt akışıyla açılıyor.", nameof(command));
        }

        for (var attempt = 1; ; attempt++)
        {
            var now = clock.UtcNow;
            var account = Account.OpenBusiness(Guid.NewGuid(), AccountNumber.New(), now);

            await using var db = await contextFactory.CreateDbContextAsync(ct);

            db.Accounts.Add(account);
            db.AccountMembers.Add(AccountMember.Of(account.Id, command.Subject, now));

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException exception) when (
                AccountNumberCollision.Is(exception) && attempt < AccountNumberCollision.MaxAttempts)
            {
                // Numara başka bir hesabınkiyle çakıştı; yeni numarayla baştan.
                continue;
            }

            return new OpenAccountResult(account.Id, account.Number, account.Type, account.CreatedAt);
        }
    }
}
