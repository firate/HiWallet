using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Kimlik bu hesap ya da cüzdan üzerinde işlem yapabilir mi. Müşteri yalnızca
/// kullanıcısı olduğu hesaba erişiyor.
///
/// Kullanıcısı olunmayan kaynak YOK sayılıyor ve <c>404</c> dönüyor, <c>403</c> değil:
/// başkasının cüzdanının var olduğu bilgisi de dışarı verilmiyor.
///
/// Kontrol uçta, akışın içinde değil: transfer ve promo çekirdeği kimin çağırdığını
/// bilmiyor. Kural çağırana göre değişiyor; çalışan müşterinin hesabında işlem
/// yapabiliyor ve onun kuralı ayrı.
/// </summary>
public sealed class AccountAccess(IDbContextFactory<WalletDbContext> contextFactory)
{
    public async Task EnsureAccountAsync(string subject, Guid accountId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var member = await db.AccountMembers
            .AsNoTracking()
            .AnyAsync(m => m.Subject == subject && m.AccountId == accountId, ct);

        if (!member)
        {
            throw new AccountNotFoundException(accountId);
        }
    }

    public async Task EnsureWalletAsync(string subject, Guid walletId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var member = await db.LedgerAccounts
            .AsNoTracking()
            .Where(w => w.Id == walletId && w.Type == LedgerAccountType.UserWallet)
            .Join(
                db.AccountMembers.AsNoTracking().Where(m => m.Subject == subject),
                wallet => wallet.AccountId,
                membership => (Guid?)membership.AccountId,
                (wallet, _) => wallet.Id)
            .AnyAsync(ct);

        if (!member)
        {
            throw new WalletNotFoundException(walletId);
        }
    }
}
