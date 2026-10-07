using HiWallet.Shared.Contracts.CardTopups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.CardTopups;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.CardTopups;

/// <param name="LedgerTransactionId">Ödendiyse paranın yazıldığı işlem; tekrar gelen mesajda orijinali.</param>
/// <param name="Replayed">Bu kapanış daha önce işlenmişti; hiçbir şeye dokunulmadı.</param>
public readonly record struct ProcessCardTopupResult(CardTopupOutcome Outcome, Guid? LedgerTransactionId, bool Replayed);

/// <summary>
/// Mesaj kalıcı olarak işlenemez ve bir ALARM: kart yüklemesi servisi ile wallet
/// ayrışmış. Tüketici mesajı kuyruğa geri koymuyor, dead-letter'a yolluyor; tek aktif
/// tüketicili kuyruk aksi halde süresiz tıkanırdı.
/// </summary>
public sealed class CardTopupRejectedException(Guid cardTopupId, string reason)
    : Exception($"Kartla yükleme kapanışı işlenemiyor ({cardTopupId}): {reason}")
{
    public Guid CardTopupId { get; } = cardTopupId;
}

/// <summary>
/// Kartla yüklemenin kapanışını ledger'a yansıtır.
/// <list type="bullet">
/// <item><b>Ödendi:</b> para cüzdana yazılıyor; cüzdan +, sağlayıcının <c>clearing</c>'i −,
/// kovası sağlayıcının kovası (kart). Seviye limiti YENİDEN kontrol edilmiyor: ödeme
/// başlarken pay ayrıldı ve o günden beri hesaba gelen her para onu saymış durumda. Kart
/// limitte öncelikli.</item>
/// <item><b>Ödenmedi:</b> ledger'a hiçbir şey yazılmıyor, pay serbest kalıyor.</item>
/// </list>
///
/// <b>Kapı kapanış satırı.</b> Pay başına tek kapanış ve <c>ON CONFLICT DO NOTHING</c> ile
/// yazılıyor: ödemenin sonucu hem sağlayıcının bildirimiyle hem kart yüklemesi servisinin
/// taramasıyla gelebilir, broker da aynı mesajı iki kez teslim edebilir. Kapanış, ledger ve
/// bakiye tek transaction'da.
///
/// <b>Aktör müşteri:</b> yüklemeyi hesabın sahibi başlattı (decisions.md madde 34); hesap
/// payın kaydında.
/// </summary>
public sealed class ProcessCardTopupHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    ProviderPolicy providers,
    IClock clock,
    ILogger<ProcessCardTopupHandler> logger)
{
    /// <summary>
    /// Aynı sağlayıcının yüklemeleri aynı clearing satırını güncelliyor; eşzamanlı ikisinden
    /// birini optimistic lock reddediyor. Taze okumayla yeniden deneniyor (decisions.md madde 9).
    /// </summary>
    private const int MaxAttempts = 3;

    public async Task<ProcessCardTopupResult> HandleAsync(CardTopupClosed message, CancellationToken ct)
    {
        if (!CardTopupOutcomes.TryFromText(message.Outcome, out var outcome))
        {
            throw new CardTopupRejectedException(message.CardTopupId, $"Bilinmeyen sonuç: '{message.Outcome}'.");
        }

        var hold = await HoldAsync(message, ct);

        // Kart yüklemesi servisi payın yazıldığından emin olamadan kapatabiliyor: pay isteği
        // cevapsız kaldıysa ödeme hiç açılmadı. Ödenmedi kapanışı o zaman zararsız, para
        // hareket etmedi ve serbest bırakılacak pay yok.
        if (hold is null)
        {
            if (outcome is CardTopupOutcome.Paid)
            {
                throw new CardTopupRejectedException(message.CardTopupId, "Payı olmayan yüklemenin parası geldi.");
            }

            logger.LogInformation(
                "Payı yazılmamış kartla yükleme ödenmeden kapandı, yapılacak bir şey yok. {CardTopupId}",
                message.CardTopupId);

            return new ProcessCardTopupResult(CardTopupOutcome.Failed, null, Replayed: true);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return outcome is CardTopupOutcome.Paid
                    ? await PayAsync(message, hold, ct)
                    : await ReleaseAsync(message, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogDebug(
                    "Kartla yükleme yazılırken çakışma, yeniden deneniyor. {CardTopupId}, deneme {Attempt}/{Max}",
                    message.CardTopupId, attempt, MaxAttempts);
            }
        }
    }

    /// <summary>
    /// Ödeme başlarken yazılan pay. Mesajdaki tutar, para birimi ve sağlayıcı onunla aynı
    /// olmak zorunda: farklıysa iki servis aynı ödeme için farklı şey biliyor.
    /// </summary>
    /// <returns>Pay; bu kimlikte pay yoksa <c>null</c>.</returns>
    private async Task<CardTopupHold?> HoldAsync(CardTopupClosed message, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var hold = await db.CardTopupHolds.AsNoTracking().FirstOrDefaultAsync(h => h.Id == message.CardTopupId, ct);

        if (hold is null)
        {
            return null;
        }

        if (hold.Provider != message.Provider
            || hold.Currency.Code != message.Currency
            || hold.Amount != message.Amount)
        {
            throw new CardTopupRejectedException(
                message.CardTopupId,
                $"Kapanış payla uyuşmuyor: pay {hold.Amount} {hold.Currency} ({hold.Provider}), " +
                $"mesaj {message.Amount} {message.Currency} ({message.Provider}).");
        }

        return hold;
    }

    private async Task<ProcessCardTopupResult> ReleaseAsync(CardTopupClosed message, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        if (!await CardTopupClosures.CloseAsFailedAsync(db, message.CardTopupId, clock.UtcNow, ct))
        {
            var existing = await CardTopupClosures.ReadAsync(db, message.CardTopupId, ct);

            if (existing.Outcome is CardTopupOutcome.Paid)
            {
                throw new CardTopupRejectedException(
                    message.CardTopupId, "Parası cüzdana yazılmış yükleme için ödenmedi bildirimi geldi.");
            }

            return new ProcessCardTopupResult(CardTopupOutcome.Failed, null, Replayed: true);
        }

        logger.LogInformation("Kartla yükleme ödenmeden kapandı, pay serbest. {CardTopupId}", message.CardTopupId);

        return new ProcessCardTopupResult(CardTopupOutcome.Failed, null, Replayed: false);
    }

    private async Task<ProcessCardTopupResult> PayAsync(CardTopupClosed message, CardTopupHold hold, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        // --- Idempotency kapısı --------------------------------------------------------
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO card_topup_hold_closures (hold_id, outcome, ledger_transaction_id, closed_at)
             VALUES ({hold.Id}, {CardTopupOutcome.Paid.ToText()}, {transactionId}, {now})
             ON CONFLICT (hold_id) DO NOTHING
             """,
            ct);

        if (inserted == 0)
        {
            var existing = await CardTopupClosures.ReadAsync(db, hold.Id, ct);

            await transaction.RollbackAsync(ct);

            // Pay serbest bırakıldıktan sonra gelen para: limit artık onu saymıyor ve arada
            // başka para girmiş olabilir. Kendiliğinden yazılmıyor; kart parası sağlayıcıda.
            if (existing.Outcome is CardTopupOutcome.Failed)
            {
                throw new CardTopupRejectedException(
                    hold.Id, "Ödenmedi diye kapanmış yüklemenin parası geldi.");
            }

            logger.LogInformation(
                "Kartla yükleme zaten işlenmiş, atlanıyor. {CardTopupId} → {TransactionId}",
                hold.Id, existing.LedgerTransactionId);

            return new ProcessCardTopupResult(CardTopupOutcome.Paid, existing.LedgerTransactionId, Replayed: true);
        }

        // Tarifesi yazılmamış sağlayıcı KALICI hata sayılmıyor: kendi konfigürasyon eksiğimiz
        // yüzünden müşterinin parası dead-letter'a gitmemeli. İstisna yukarı çıkıyor, mesaj
        // kuyrukta bekliyor, tarife eklenince işleniyor. Kapanış satırı transaction'la birlikte
        // geri alınıyor.
        var terms = providers.For(hold.Provider);

        var clearing = await db.LedgerAccounts
                           .AsNoTracking()
                           .FirstOrDefaultAsync(
                               a => a.Type == LedgerAccountType.Clearing
                                    && a.Provider == hold.Provider
                                    && a.Currency == hold.Currency,
                               ct)
                       ?? throw new CardTopupRejectedException(
                           hold.Id, $"'{hold.Provider}' sağlayıcısının {hold.Currency} clearing hesabı yok.");

        var wallet = await db.LedgerAccounts.AsNoTracking().SingleAsync(a => a.Id == hold.WalletId, ct);

        // --- Ledger --------------------------------------------------------------------
        // Anahtar sağlayıcı ve kart yüklemesi (decisions.md madde 27'deki gerekçe): kart
        // yüklemesinin kimliği sağlayıcı bazında değil global ama ön ek okuyana kaynağı söylüyor.
        var tx = LedgerTransaction.Create(
            transactionId, LedgerTransactionType.Topup, hold.WalletId,
            Actor.Customer(hold.AccountId), now, $"{hold.Provider}:{hold.Id}");

        // Kova sağlayıcıdan (decisions.md madde 36): kart parası card, IBAN'a çıkmıyor.
        tx.AddEntry(hold.WalletId, hold.Money, terms.FundType);
        tx.AddEntry(clearing.Id, hold.Money.Negated, terms.FundType);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // --- Sağlayıcı ücreti (ledger DEĞİL) ---------------------------------------------
        // Beklenen ücret ledger'la aynı transaction'da; gerçekleşen tutar settlement ya da
        // faturayla geliyor (decisions.md madde 10 ve 11).
        db.ProviderFees.Add(new ProviderFee
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Provider = terms.Provider,
            SettlementModel = terms.FeeSettlement,
            ExpectedAmount = terms.Fee.Expected(hold.Money).Amount,
            Currency = hold.Currency.Code,
            ProviderRef = message.ProviderRef,
            OccurredAt = now
        });

        // --- Projeksiyon ---------------------------------------------------------------
        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8).
        var canGoNegative = new Dictionary<Guid, bool>
        {
            [wallet.Id] = wallet.CanGoNegative,
            [clearing.Id] = clearing.CanGoNegative
        };

        foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(
                                  b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct)
                          ?? throw new InvalidOperationException($"Bakiye satırı yok: {entry.LedgerAccountId}");

            balance.Apply(entry.Money, canGoNegative[entry.LedgerAccountId], now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Kartla yükleme cüzdana yazıldı. {CardTopupId} → {TransactionId}, cüzdan {WalletId} +{Amount} {Currency}",
            hold.Id, transactionId, hold.WalletId, hold.Amount, hold.Currency.Code);

        return new ProcessCardTopupResult(CardTopupOutcome.Paid, transactionId, Replayed: false);
    }
}
