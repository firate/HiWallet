using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.CardTopups;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.CardTopups;

/// <param name="HoldId">Kart yüklemesinin kimliği; aynı kimlikle tekrar aynı payı döner.</param>
/// <param name="Provider">Kart sağlayıcısı; parası yazılırken clearing hesabı onunki.</param>
public sealed record PlaceCardTopupHoldCommand(Guid HoldId, Guid WalletId, decimal Amount, string Currency, string Provider);

public sealed record CardTopupHoldResult(Guid HoldId, Guid AccountId, Guid WalletId, Money Amount, bool Replayed);

/// <summary>
/// Kartla yüklemenin başlangıcı: seviye limiti kontrol ediliyor ve tutar limitten
/// ayrılıyor (<see cref="CardTopupHold"/>). Limit yetmiyorsa pay yazılmıyor ve kart yüklemesi
/// servisi ödemeyi hiç açmıyor; kart çekilmeden reddediliyor.
///
/// Sahiplik uçta kontrol ediliyor (<see cref="AccountAccess"/>), burada değil.
///
/// <b>Kapı limitten ÖNCE değil, ama kararın içinde.</b> Hesabın satırı kilitleniyor, ayın
/// girişi okunuyor, pay <c>ON CONFLICT DO NOTHING</c> ile yazılıyor. Satır yazılmadıysa bu
/// kimlikte pay zaten var: tekrar eden istek limit yeniden değerlendirilmeden mevcut payı
/// dönüyor (transferdeki gibi; aksi halde kendi payı onu limitin dışına iterdi). Yazıldıysa
/// limit, payın yazılmadan önceki sayımla kontrol ediliyor ve aşımda transaction geri
/// alınıyor.
/// </summary>
public sealed class PlaceCardTopupHoldHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    KycLimitPolicy kycLimits,
    IClock clock,
    ILogger<PlaceCardTopupHoldHandler> logger)
{
    public async Task<CardTopupHoldResult> HandleAsync(PlaceCardTopupHoldCommand command, CancellationToken ct)
    {
        var currency = Currency.From(command.Currency);
        var amount = new Money(command.Amount, currency);

        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Kartla yükleme tutarı pozitif olmalı.", nameof(command));
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var wallet = await db.LedgerAccounts
                         .AsNoTracking()
                         .FirstOrDefaultAsync(a => a.Id == command.WalletId && a.Type == LedgerAccountType.UserWallet, ct)
                     ?? throw new WalletNotFoundException(command.WalletId);

        if (wallet.Currency != currency)
        {
            throw new AccountRuleException($"Cüzdan {wallet.Currency} tutuyor, {currency} ile yüklenemez.");
        }

        var accountId = wallet.AccountId
                        ?? throw new InvalidOperationException($"Cüzdan {wallet.Id} bir hesaba bağlı değil.");

        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Type, a.KycLevel })
            .SingleAsync(ct);

        // Havaleyle yüklemedeki gibi: işyerinin seviyesi ve limiti yok.
        if (account.Type is not AccountType.Person || account.KycLevel is not { } level)
        {
            throw new AccountRuleException("Kartla yükleme bireysel hesapta.");
        }

        await IncomingUsage.LockAsync(db, accountId, ct);

        var now = clock.UtcNow;
        var received = await IncomingUsage.ThisMonthAsync(db, accountId, currency, now, ct);
        var balance = await IncomingUsage.BalanceAsync(db, accountId, currency, ct);

        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO card_topup_holds (id, account_id, wallet_id, amount, currency, provider, created_at)
             VALUES ({command.HoldId}, {accountId}, {wallet.Id}, {amount.Amount}, {currency.Code}, {command.Provider}, {now})
             ON CONFLICT (id) DO NOTHING
             """,
            ct);

        if (inserted == 0)
        {
            await transaction.RollbackAsync(ct);

            return await ExistingAsync(command, ct);
        }

        try
        {
            kycLimits.EnsureIncoming(level, KycMovement.Deposit, amount, received, balance);
        }
        catch (IncomingLimitExceededException exception)
        {
            logger.LogInformation(
                "Kartla yükleme seviye limitine takıldı, ödeme açılmıyor. Hesap {AccountId}, kural {Rule}",
                accountId, exception.LimitName);

            throw new CardTopupLimitExceededException(exception.LimitName, exception.Limit);
        }

        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Kartla yükleme payı ayrıldı. {HoldId}, hesap {AccountId}, {Amount} {Currency}",
            command.HoldId, accountId, amount.Amount, currency.Code);

        return new CardTopupHoldResult(command.HoldId, accountId, wallet.Id, amount, Replayed: false);
    }

    /// <summary>
    /// Bu kimlikte pay zaten var. Aynı istek olmak zorunda: aynı kimlikle başka bir cüzdan ya
    /// da tutarla gelen istek kart yüklemesi servisindeki bir hata, sessizce mevcut payı
    /// dönmek onu gizlerdi. İş kuralı reddi → <c>422</c>.
    /// </summary>
    private async Task<CardTopupHoldResult> ExistingAsync(PlaceCardTopupHoldCommand command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var hold = await db.CardTopupHolds.AsNoTracking().SingleAsync(h => h.Id == command.HoldId, ct);

        if (hold.WalletId != command.WalletId
            || hold.Amount != command.Amount
            || hold.Currency.Code != command.Currency
            || hold.Provider != command.Provider)
        {
            throw new AccountRuleException($"Kartla yükleme payı {command.HoldId} başka bir istekle yazılmış.");
        }

        return new CardTopupHoldResult(hold.Id, hold.AccountId, hold.WalletId, hold.Money, Replayed: true);
    }
}
