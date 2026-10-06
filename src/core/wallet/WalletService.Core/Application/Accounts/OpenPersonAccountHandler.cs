using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Balances;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class OpenPersonAccountHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock)
{
    /// <summary>Kayıtla birlikte açılan cüzdanın adı.</summary>
    public const string FirstWalletName = "Ana";

    public async Task<OpenPersonAccountResult> HandleAsync(OpenPersonAccountCommand command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await OpenAsync(command, ct);
            }
            catch (Exception exception) when (
                AccountNumberCollision.Is(exception) && attempt < AccountNumberCollision.MaxAttempts)
            {
                // Numara başka bir hesabınkiyle çakıştı; transaction geri alındı, yeni numarayla baştan.
            }
        }
    }

    private async Task<OpenPersonAccountResult> OpenAsync(OpenPersonAccountCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var account = Account.OpenPerson(Guid.NewGuid(), AccountNumber.New(), command.Holder, now);
        var currency = SystemAccounts.DefaultCurrency;

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Tekillik veritabanında: ux_accounts_person_holder. "Önce bak sonra yaz" iki
        // eşzamanlı açılışı birlikte geçirirdi (CLAUDE.md "Idempotency"). Metinler
        // ValueConverters'taki eşlemenin ve ck_accounts_kyc_level'ın aynısı.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO accounts (id, number, type, holder, kyc_level, accepts_promo, created_at)
             VALUES ({account.Id}, {account.Number.Value}, 'person', {command.Holder}, 'unknown', false, {now})
             ON CONFLICT (holder) WHERE holder IS NOT NULL DO NOTHING
             """,
            ct);

        if (inserted == 0)
        {
            await transaction.RollbackAsync(ct);

            return await ExistingAsync(command.Holder, ct);
        }

        // Üyelik, cüzdan ve bakiye satırları hesapla AYNI transaction'da: kullanıcısız ya
        // da cüzdansız bir hesap kalmıyor, tekrar eden açılış da yarım bir hesabı
        // tamamlamaya çalışmıyor.
        db.AccountMembers.Add(AccountMember.Of(account.Id, command.Holder, now));

        var wallet = LedgerAccount.Wallet(Guid.NewGuid(), account.Id, FirstWalletName, currency, now);
        db.LedgerAccounts.Add(wallet);

        foreach (var fundType in FundTypes.All)
        {
            db.LedgerBalances.Add(LedgerBalance.OpenFor(wallet.Id, currency, fundType, now));
        }

        // İlk cüzdan para biriminin varsayılanı: hesap numarasına gelen para buraya.
        db.DefaultWallets.Add(DefaultWallet.Of(wallet));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new OpenPersonAccountResult(
            account.Id, account.Number, KycLevel.Unknown, wallet.Id, now, Replayed: false);
    }

    private async Task<OpenPersonAccountResult> ExistingAsync(string holder, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var existing = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Holder == holder)
            .Select(a => new
            {
                a.Id,
                a.Number,
                a.KycLevel,
                a.CreatedAt,
                // Açılışla gelen cüzdan: hesabın ilk cüzdanı.
                WalletId = db.LedgerAccounts
                    .Where(w => w.AccountId == a.Id)
                    .OrderBy(w => w.CreatedAt)
                    .Select(w => w.Id)
                    .First()
            })
            .SingleAsync(ct);

        return new OpenPersonAccountResult(
            existing.Id, existing.Number, existing.KycLevel!.Value, existing.WalletId, existing.CreatedAt, Replayed: true);
    }
}
