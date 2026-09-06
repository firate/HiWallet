using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Balances;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Accounts;

public sealed class OpenWalletHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IClock clock)
{
    public async Task<OpenWalletResult> HandleAsync(OpenWalletCommand command, CancellationToken ct)
    {
        var currency = Currency.From(command.Currency);

        // Sistem hesapları para birimi başına açılıyor ve bugün yalnızca TRY var.
        // Başka bir para biriminde cüzdan yaratılabilirdi ama ilk transferde
        // "clearing hesabı yok" ile patlardı — hatayı açılışa çekiyoruz.
        if (currency != SystemAccounts.DefaultCurrency)
        {
            throw new UnsupportedCurrencyException(currency, SystemAccounts.DefaultCurrency);
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // Hesap yoksa cüzdan da olmamalı: FK zaten engellerdi ama Postgres hatası
        // 500'e düşerdi. Burada bakınca 404 dönüyor.
        var accountExists = await db.Accounts
            .AnyAsync(a => a.Id == command.AccountId, ct);

        if (!accountExists)
        {
            throw new AccountNotFoundException(command.AccountId);
        }

        var now = clock.UtcNow;
        var wallet = LedgerAccount.Wallet(Guid.NewGuid(), command.AccountId, command.Name, currency, now);

        db.LedgerAccounts.Add(wallet);

        // Bakiye satırı cüzdanla AYNI transaction'da açılıyor. Tembel açılsaydı ilk
        // transfer "Bakiye satırı yok" ile patlardı — handler'lar satırın varlığını
        // varsayıyor, yaratmıyor.
        db.LedgerBalances.Add(LedgerBalance.OpenFor(wallet.Id, currency, now));

        await db.SaveChangesAsync(ct);

        return new OpenWalletResult(
            wallet.Id, wallet.AccountId!.Value, wallet.Name!, wallet.Currency.Code, 0m, wallet.CreatedAt);
    }
}
