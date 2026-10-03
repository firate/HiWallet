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

        // Bakiye satırları cüzdanla AYNI transaction'da açılıyor. Tembel açılsaydı ilk
        // transfer "Bakiye satırı yok" ile patlardı — handler'lar satırın varlığını
        // varsayıyor, yaratmıyor.
        //
        // Kova başına bir satır (decisions.md madde 36). Üçü birden açılıyor: biri
        // eksik kalsaydı o kovaya ilk yazma anında aynı hata dönerdi.
        foreach (var fundType in FundTypes.All)
        {
            db.LedgerBalances.Add(LedgerBalance.OpenFor(wallet.Id, currency, fundType, now));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);

        // Hesabın bu para birimindeki ilk cüzdanıysa varsayılan oluyor. "Önce bak sonra
        // yaz" eşzamanlı iki açılışı birlikte varsayılan yapmaya kalkardı; anahtar
        // (hesap, para birimi) ilkini tutuyor (CLAUDE.md "Idempotency").
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO default_wallets (account_id, currency, wallet_id)
             VALUES ({wallet.AccountId}, {currency.Code}, {wallet.Id})
             ON CONFLICT (account_id, currency) DO NOTHING
             """,
            ct);

        await transaction.CommitAsync(ct);

        return new OpenWalletResult(
            wallet.Id, wallet.AccountId!.Value, wallet.Name!, wallet.Currency.Code, 0m, wallet.CreatedAt);
    }
}
