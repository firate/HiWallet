using HiWallet.Shared.Contracts.Deposits;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Deposits;

/// <summary>
/// Banka hesabımıza gelen havaleyi ledger'a yazar. Para mesaj geldiğinde ZATEN
/// bankamızda; burada verilen karar parayı kabul etmek değil, nereye yazılacağı:
/// <list type="bullet">
/// <item><b>Cüzdan:</b> açıklamada tek bir geçerli hesap numarası var, hesap bireysel,
/// bu para biriminde varsayılan cüzdanı var, gönderen hesabın sahibi ve seviyenin
/// limiti yetiyor. Cüzdan +, bankanın nostro'su −.</item>
/// <item><b>Askı:</b> geri kalan her durum. Askı +, nostro −; sebep
/// <c>suspended_deposits</c>'te. Para kaynağına iade edilene ya da bir cüzdana
/// geçirilene kadar orada.</item>
/// </list>
/// İki yolda da nostro aynı tutarla hareket ediyor: banka bakiyesiyle ledger her durumda
/// tutuyor. Reddetmek, yani hiç yazmamak, ledger'ı bankadan ayırırdı.
///
/// <b>Yalnızca müşterinin kendi hesabından.</b> Gönderenin kimlik numarası hesabın
/// sahibininkiyle karşılaştırılıyor; soru onboarding'e soruluyor, numara wallet'ta
/// tutulmuyor (<see cref="IHolderIdentity"/>). Bildirimde numara yoksa doğrulanamıyor
/// ve para askıya düşüyor.
///
/// <b>Eşleştirme transaction'ın DIŞINDA.</b> Kimlik sorusu bir ağ çağrısı; açık bir
/// transaction'ı ve tuttuğu satırları bekletmemeli. Kapı, limit ve ledger tek
/// transaction'da (top-up'taki gibi): ayrı commit'lerde aradaki çökme ya "yazıldı ama
/// işlenmedi sayıldı" ya da tersini bırakırdı.
///
/// Sağlayıcı ücreti YOK: gelen havalenin bankada alıcıya bir ücreti yok, <c>provider_fees</c>
/// satırı yazılmıyor.
/// </summary>
public sealed class ProcessDepositHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    IHolderIdentity holders,
    KycLimitPolicy kycLimits,
    IClock clock,
    ILogger<ProcessDepositHandler> logger)
{
    /// <summary>
    /// Bütün havaleler aynı nostro satırını güncelliyor; eşzamanlı ikisinden birini
    /// optimistic lock reddediyor. Taze okumayla yeniden deneniyor (decisions.md madde 9).
    /// </summary>
    private const int MaxAttempts = 3;

    public async Task<ProcessDepositResult> HandleAsync(BankDepositReceived message, CancellationToken ct)
    {
        var amount = ParseAmount(message);
        var target = await MatchAsync(message, amount.Currency, ct);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RecordAsync(message, amount, target, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogDebug(
                    "Havale yazılırken çakışma, yeniden deneniyor. {Provider}/{BankReference}, deneme {Attempt}/{Max}",
                    message.Provider, message.BankReference, attempt, MaxAttempts);
            }
        }
    }

    /// <summary>
    /// Paranın kime ait olduğu: açıklamadaki numara, hesap ve gönderenin kimliği. Limit
    /// burada değil, transaction'ın içinde: ayın toplamı kayıtla aynı anda okunmalı.
    /// </summary>
    private async Task<Target> MatchAsync(BankDepositReceived message, Currency currency, CancellationToken ct)
    {
        var numbers = AccountNumber.FindIn(message.Description);

        if (numbers.Count == 0) return Target.Held(DepositHoldReason.NoAccountNumber);
        if (numbers.Count > 1) return Target.Held(DepositHoldReason.AmbiguousAccountNumber);

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var number = numbers[0];
        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Number == number)
            .Select(a => new { a.Id, a.Type, a.Holder, a.KycLevel })
            .SingleOrDefaultAsync(ct);

        if (account is null) return Target.Held(DepositHoldReason.UnknownAccount);

        // İşyerinin seviyesi ve onboarding'de sahibi yok: kimin gönderdiğini doğrulayacak
        // bir karşılık da yok.
        if (account.Type is not AccountType.Person || account.Holder is null || account.KycLevel is null)
        {
            return Target.Held(DepositHoldReason.BusinessAccount, account.Id);
        }

        var walletId = await db.DefaultWallets
            .Where(d => d.AccountId == account.Id && d.Currency == currency)
            .Select(d => (Guid?)d.WalletId)
            .SingleOrDefaultAsync(ct);

        if (walletId is null) return Target.Held(DepositHoldReason.NoWalletInCurrency, account.Id);

        if (string.IsNullOrWhiteSpace(message.SenderNationalId))
        {
            return Target.Held(DepositHoldReason.UnknownSender, account.Id);
        }

        // Cevapsız kalırsa istisna yukarı çıkıyor ve mesaj kuyruğa dönüyor: tahminle
        // ne cüzdana ne askıya yazılıyor.
        if (!await holders.IsHolderAsync(account.Holder, message.SenderNationalId, ct))
        {
            return Target.Held(DepositHoldReason.SenderNotHolder, account.Id);
        }

        return Target.Wallet(account.Id, walletId.Value, account.KycLevel.Value);
    }

    private async Task<ProcessDepositResult> RecordAsync(
        BankDepositReceived message, Money amount, Target target, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var nostro = await SystemAccountAsync(db, message, LedgerAccountType.Nostro, amount.Currency, ct);
        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        // --- Idempotency kapısı -------------------------------------------------------
        // Anahtar bankanın gelen işlem referansı: aynı havale bildirimle de taramayla da
        // gelebilir. "Önce SELECT sonra INSERT" YOK (CLAUDE.md).
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO processed_events (provider, event_id, processed_at, ledger_transaction_id)
             VALUES ({message.Provider}, {message.BankReference}, {now}, {transactionId})
             ON CONFLICT (provider, event_id) DO NOTHING
             """,
            ct);

        if (inserted == 0)
        {
            var original = await db.ProcessedEvents
                .AsNoTracking()
                .Where(e => e.Provider == message.Provider && e.EventId == message.BankReference)
                .Select(e => e.LedgerTransactionId)
                .FirstAsync(ct);

            await transaction.RollbackAsync(ct);

            logger.LogInformation(
                "Havale zaten işlenmiş, atlanıyor. {Provider}/{BankReference} → {TransactionId}",
                message.Provider, message.BankReference, original);

            return new ProcessDepositResult(
                original ?? throw new InvalidOperationException(
                    $"İşlenmiş havalenin ledger işlemi yok: {message.Provider}/{message.BankReference}"),
                Replayed: true,
                HeldFor: null);
        }

        // --- Limit --------------------------------------------------------------------
        // Seviyenin aylık limiti, ayın toplam girişi ve bakiye tavanı. Aşan havale
        // reddedilmiyor, askıya düşüyor: para zaten bankamızda.
        if (target.WalletId is not null)
        {
            var received = await IncomingUsage.ThisMonthAsync(db, target.AccountId!.Value, amount.Currency, now, ct);
            var balance = await IncomingUsage.BalanceAsync(db, target.AccountId.Value, amount.Currency, ct);

            try
            {
                kycLimits.EnsureIncoming(target.Level!.Value, KycMovement.Deposit, amount, received, balance);
            }
            catch (IncomingLimitExceededException exception)
            {
                logger.LogInformation(
                    "Havale seviye limitine takıldı, askıya alınıyor. {Provider}/{BankReference}, kural {Rule}",
                    message.Provider, message.BankReference, exception.LimitName);

                target = Target.Held(DepositHoldReason.LimitExceeded, target.AccountId);
            }
        }

        // --- Ledger -------------------------------------------------------------------
        var idempotencyKey = $"{message.Provider}:{message.BankReference}";
        LedgerTransaction tx;

        if (target.WalletId is { } walletId)
        {
            tx = LedgerTransaction.Create(
                transactionId, LedgerTransactionType.Topup, walletId,
                Actor.Customer(target.AccountId!.Value), now, idempotencyKey);

            tx.AddEntry(walletId, amount, FundType.Cash);
        }
        else
        {
            var suspense = await SystemAccountAsync(db, message, LedgerAccountType.Suspense, amount.Currency, ct);

            tx = LedgerTransaction.Create(
                transactionId, LedgerTransactionType.SuspendedDeposit, suspense.Id,
                SystemActors.Deposit, now, idempotencyKey);

            tx.AddEntry(suspense.Id, amount, FundType.Cash);

            db.SuspendedDeposits.Add(SuspendedDeposit.Of(
                transactionId, message.Provider, message.BankReference, amount,
                target.Reason!.Value, target.AccountId, message.ReceivedAt, now));
        }

        // Havale cash (decisions.md madde 36); nostro bacağı da aynı kovada.
        tx.AddEntry(nostro.Id, amount.Negated, FundType.Cash);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // --- Projeksiyon --------------------------------------------------------------
        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8). Negatife
        // düşebilirlik hesabın tipinden (decisions.md madde 6).
        var affectedIds = tx.Entries.Select(e => e.LedgerAccountId).ToArray();
        var affected = await db.LedgerAccounts
            .AsNoTracking()
            .Where(a => affectedIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(
                                  b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct)
                          ?? throw new InvalidOperationException($"Bakiye satırı yok: {entry.LedgerAccountId}");

            balance.Apply(entry.Money, affected[entry.LedgerAccountId].CanGoNegative, now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Kişisel veri log'a YAZILMIYOR: ne açıklama ne kimlik numarası.
        logger.LogInformation(
            "Havale işlendi. {Provider}/{BankReference} → {TransactionId}, {Amount} {Currency}, {Outcome}",
            message.Provider, message.BankReference, transactionId, amount.Amount, amount.Currency.Code,
            target.Reason is { } reason ? $"askı ({reason.ToText()})" : "cüzdan");

        return new ProcessDepositResult(transactionId, Replayed: false, target.Reason);
    }

    private static Money ParseAmount(BankDepositReceived message)
    {
        if (message.Amount <= 0m)
        {
            throw new DepositRejectedException(
                message.Provider, message.BankReference, $"Tutar pozitif olmalı: {message.Amount}");
        }

        try
        {
            return new Money(message.Amount, Currency.From(message.Currency));
        }
        catch (ArgumentException exception)
        {
            throw new DepositRejectedException(message.Provider, message.BankReference, exception.Message);
        }
    }

    private static async Task<LedgerAccount> SystemAccountAsync(
        WalletDbContext db, BankDepositReceived message, LedgerAccountType type, Currency currency, CancellationToken ct)
    {
        return await db.LedgerAccounts
                   .AsNoTracking()
                   .FirstOrDefaultAsync(
                       a => a.Type == type && a.Provider == message.Provider && a.Currency == currency, ct)
               ?? throw new DepositRejectedException(
                   message.Provider, message.BankReference,
                   $"Bu bankanın {currency} {type} hesabı yok.");
    }

    /// <summary>Havalenin gideceği yer: cüzdan ya da askı.</summary>
    private readonly record struct Target(
        Guid? AccountId, Guid? WalletId, KycLevel? Level, DepositHoldReason? Reason)
    {
        public static Target Wallet(Guid accountId, Guid walletId, KycLevel level) =>
            new(accountId, walletId, level, null);

        public static Target Held(DepositHoldReason reason, Guid? accountId = null) =>
            new(accountId, null, null, reason);
    }
}
