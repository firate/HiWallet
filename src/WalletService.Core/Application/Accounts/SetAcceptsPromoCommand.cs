using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// İşyerinin platform fonlu promo kabulü (decisions.md madde 37). Backoffice'ten,
/// pazarlama rolüyle. Değişiklik bundan sonraki ödemeleri etkiliyor; verilmiş partiler
/// ve geçmiş ödemeler olduğu gibi kalıyor.
/// </summary>
public sealed record SetAcceptsPromoCommand(Guid AccountId, bool AcceptsPromo);

public sealed class SetAcceptsPromoHandler(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task HandleAsync(SetAcceptsPromoCommand command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == command.AccountId, ct)
                      ?? throw new AccountNotFoundException(command.AccountId);

        account.SetAcceptsPromo(command.AcceptsPromo);

        await db.SaveChangesAsync(ct);
    }
}
