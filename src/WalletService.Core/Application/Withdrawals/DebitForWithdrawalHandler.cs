using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Withdrawals;

/// <summary>
/// Çekim için cüzdandan para düşer (overview.md madde 6). Saga'nın wallet tarafındaki
/// ilk adımı.
///
/// <b>ÜÇ bacak</b> (<c>ledger-schema.md</c> "Withdrawal"). 100 çekim, 2 komisyon:
/// <code>
///   user_wallet  -102     müşteriden çıkan toplam
///   clearing     +100     bankaya ödenecek, yolda
///   revenue        +2     komisyon geliri
///   toplam          0
/// </code>
/// Komisyon sıfırsa <c>revenue</c> bacağı yazılmıyor; iki bacak da dengeli.
///
/// <b>Reddetme bir hata DEĞİL.</b> Yetersiz bakiye ve limit aşımı beklenen sonuçlar:
/// ledger'a hiçbir şey yazılmıyor, saga'ya <see cref="WithdrawalDebitRejected"/>
/// dönülüyor ve mesaj ack'leniyor. Dead-letter'a gitseydi saga cevabı hiç alamaz ve
/// müşteri sonsuza kadar "işleniyor" görürdü.
/// </summary>
public sealed class DebitForWithdrawalHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    WithdrawalPolicy policy,
    IClock clock,
    ILogger<DebitForWithdrawalHandler> logger)
{
    /// <summary>decisions.md madde 9: 3 deneme, her denemede yeniden oku.</summary>
    private const int MaxAttempts = 3;

    /// <summary>
    /// Ledger'daki idempotency key. Kapsam cüzdan, anahtar saga — bir saga'nın
    /// düşmesi bir kez yazılabilir. <c>processed_messages</c>'a ek ikinci emniyet
    /// kemeri: aynı komut iki tüketici tarafından AYNI ANDA işlenirse ikincisi
    /// buradaki unique index'e takılıyor.
    /// </summary>
    public static string IdempotencyKey(Guid sagaId) => $"withdrawal:{sagaId}";

    public async Task<WithdrawalReply> HandleAsync(DebitForWithdrawal command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DebitAsync(command, ct);
            }
            catch (DomainException rejection)
            {
                // İş kuralı reddi. Tekrar denemek aynı sonucu verir; saga'ya cevap
                // dönülüp mesaj kapatılıyor.
                logger.LogInformation(
                    "Çekim reddedildi. Saga {SagaId}, cüzdan {WalletId}: {Reason}",
                    command.SagaId, command.WalletId, rejection.Message);

                return await RecordRejectionAsync(command, rejection, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Eski version ile tekrar denemek sonsuza kadar başarısız olur; retry'ın
                // anlamı YENİ anlık görüntüyle yeniden denemek (decisions.md madde 9).
                logger.LogDebug(
                    "Çekim düşmesi çakıştı, yeniden deneniyor. Deneme {Attempt}/{Max}, saga {SagaId}",
                    attempt, MaxAttempts, command.SagaId);
            }
        }
    }

    private async Task<WithdrawalReply> DebitAsync(DebitForWithdrawal command, CancellationToken ct)
    {
        var currency = Currency.From(command.Currency);
        var amount = new Money(command.Amount, currency);

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var wallet = await LoadWalletAsync(db, command, currency, ct);
        var clearing = await LoadClearingAsync(db, currency, ct);
        var revenue = await LoadRevenueAsync(db, currency, ct);

        var accountId = wallet.AccountId
                        ?? throw new InvalidOperationException($"Cüzdan {wallet.Id} bir hesaba bağlı değil.");

        // --- Politika ---------------------------------------------------------------
        var commission = policy.Commission(amount);
        var totalDebit = amount + commission;

        var spentToday = await SpentTodayAsync(db, accountId, currency, ct);

        // Kapsam hesap, cüzdan değil (decisions.md madde 20). Kontrol edilen tutar
        // komisyon DAHİL (madde 22).
        policy.EnsureWithinLimit(accountId, totalDebit, spentToday);

        // --- Projeksiyon --------------------------------------------------------------
        // Ledger'dan ÖNCE uygulanıyor, bilerek: yetersiz bakiye burada ortaya çıkıyor
        // ve henüz hiçbir şey yazılmamışken reddetme yoluna sapılabiliyor. Bellekteki
        // değişiklik SaveChanges'e kadar kalıcı değil, o yüzden sıra güvenli.
        var now = clock.UtcNow;
        var transactionId = Guid.NewGuid();

        var legs = BuildLegs(wallet, clearing, revenue, totalDebit, amount, commission);

        await ApplyToBalancesAsync(db, legs, now, ct);

        // --- Idempotency kapısı ----------------------------------------------------
        // Cevap ledger'dan ÖNCE biliniyor çünkü işlem kimliğini biz üretiyoruz —
        // top-up'taki kalıbın aynısı. "Önce SELECT sonra INSERT" YOK (CLAUDE.md).
        var reply = WithdrawalReply.For(new WithdrawalDebited
        {
            SagaId = command.SagaId,
            LedgerTransactionId = transactionId,
            TotalDebited = totalDebit.Amount
        });

        var claimed = await ClaimAsync(db, command, reply, transactionId, now, ct);

        if (!claimed)
        {
            await transaction.RollbackAsync(ct);

            return await ReadStoredReplyAsync(command, ct);
        }

        // --- Ledger -------------------------------------------------------------------
        // Aktör MÜŞTERİ, saga değil (decisions.md madde 34). Çekimi müşteri başlattı;
        // araya kuyruk girmesi bunu değiştirmiyor. Aktör "kaydı hangi taşıma getirdi"
        // sorusunun değil, "bu hareketi kim başlattı" sorusunun cevabı.
        //
        // Aynı saga'nın diğer iki kaydı `system` KALIYOR ve bu tutarsızlık değil:
        // iadeyi kimse istemedi (banka reddetti, saga karar verdi), settlement'ı da
        // banka bildirdi. Orada başlatan bir insan gerçekten yok.
        var tx = LedgerTransaction.Create(
            transactionId,
            LedgerTransactionType.Withdrawal,
            wallet.Id,
            Actor.Customer(accountId),
            now,
            IdempotencyKey(command.SagaId),
            correlationId: command.SagaId);

        foreach (var (ledgerAccountId, delta, _) in legs)
        {
            tx.AddEntry(ledgerAccountId, delta);
        }

        // DB'deki deferred trigger'dan önce, daha anlaşılır hatayla.
        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Çekim düşüldü. Saga {SagaId}, cüzdan {WalletId} -{Total} ({Amount} + {Commission} komisyon) → {TransactionId}",
            command.SagaId, wallet.Id, totalDebit.Amount, amount.Amount, commission.Amount, transactionId);

        return reply;
    }

    /// <summary>
    /// Üç bacak. Komisyon sıfırsa <c>revenue</c> bacağı YOK — sıfır tutarlı bir entry
    /// ledger'a gürültü yazmaktan başka bir şey yapmazdı.
    /// </summary>
    private static List<(Guid LedgerAccountId, Money Delta, bool CanGoNegative)> BuildLegs(
        LedgerAccount wallet,
        LedgerAccount clearing,
        LedgerAccount revenue,
        Money totalDebit,
        Money amount,
        Money commission)
    {
        var legs = new List<(Guid, Money, bool)>
        {
            (wallet.Id, totalDebit.Negated, wallet.CanGoNegative),
            (clearing.Id, amount, clearing.CanGoNegative)
        };

        if (!commission.IsZero)
        {
            legs.Add((revenue.Id, commission, revenue.CanGoNegative));
        }

        // Sıra ledger hesap kimliğine göre ARTAN — iki işlem aynı hesap çiftine ters
        // sırayla yazarsa deadlock olur (decisions.md madde 8).
        return [.. legs.OrderBy(leg => leg.Item1)];
    }

    private static async Task ApplyToBalancesAsync(
        WalletDbContext db,
        List<(Guid LedgerAccountId, Money Delta, bool CanGoNegative)> legs,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var (ledgerAccountId, delta, canGoNegative) in legs)
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(b => b.LedgerAccountId == ledgerAccountId, ct)
                          ?? throw new InvalidOperationException($"Bakiye satırı yok: {ledgerAccountId}");

            balance.Apply(delta, canGoNegative, now);
        }
    }

    /// <summary>
    /// Bugün bu hesabın TÜM cüzdanlarından çekimle çıkan NET tutar.
    ///
    /// <b>İade edilenler düşülüyor.</b> Transfer tarafındaki sayım yalnızca kendi
    /// tipinin debit bacaklarını topluyor; çekimde bu yanlış olurdu. Banka reddedip
    /// para müşteriye geri döndüyse o para hesaptan ÇIKMADI ve günlük limiti
    /// tüketmemeli — limitin koruduğu şey "bugün bu hesaptan ne kadar para çıktı"
    /// (decisions.md madde 22).
    ///
    /// İşaretli toplam alınıyor: düşme bacağı negatif, iade bacağı pozitif, ikisi
    /// birbirini götürüyor. Sonuç ters çevrilip pozitif "harcanan" olarak dönüyor.
    /// </summary>
    private async Task<Money> SpentTodayAsync(
        WalletDbContext db, Guid accountId, Currency currency, CancellationToken ct)
    {
        var since = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);

        var walletIds = db.LedgerAccounts
            .Where(a => a.AccountId == accountId)
            .Select(a => a.Id);

        var net = await db.LedgerEntries
            .Where(e => walletIds.Contains(e.LedgerAccountId)
                        && e.Currency == currency
                        && e.CreatedAt >= since
                        && db.LedgerTransactions.Any(t =>
                            t.Id == e.TransactionId
                            && (t.Type == LedgerTransactionType.Withdrawal
                                || t.Type == LedgerTransactionType.Refund)))
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new Money(-net, currency);
    }

    /// <summary>
    /// Komutu sahiplenir. <c>ON CONFLICT DO NOTHING</c> kararı tek adımda DB'ye
    /// verdiriyor; önce okunup sonra yazılsaydı iki tüketici arasında TOCTOU açığı
    /// kalırdı ve müşterinin parası iki kez düşerdi.
    /// </summary>
    /// <returns>Satır yazıldıysa <c>true</c>; komut daha önce işlenmişse <c>false</c>.</returns>
    private static async Task<bool> ClaimAsync(
        WalletDbContext db,
        DebitForWithdrawal command,
        WithdrawalReply reply,
        Guid? ledgerTransactionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO processed_messages
                 (message_id, message_type, saga_id, processed_at,
                  ledger_transaction_id, reply_routing_key, reply_payload)
             VALUES
                 ({command.CommandId}, {nameof(DebitForWithdrawal)}, {command.SagaId}, {now},
                  {ledgerTransactionId}, {reply.RoutingKey}, {reply.Payload}::jsonb)
             ON CONFLICT (message_id) DO NOTHING
             """,
            ct);

        return inserted == 1;
    }

    /// <summary>
    /// Reddi de deftere yazar. Kaydedilmeseydi tekrar teslimde kural yeniden
    /// değerlendirilir ve bu sefer bakiye yetiyorsa aynı komut para düşerdi — oysa
    /// saga çoktan "reddedildi" diye kapanmış olurdu.
    /// </summary>
    private async Task<WithdrawalReply> RecordRejectionAsync(
        DebitForWithdrawal command, DomainException rejection, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var reply = WithdrawalReply.For(new WithdrawalDebitRejected
        {
            SagaId = command.SagaId,
            Reason = rejection.Message
        });

        var claimed = await ClaimAsync(
            db, command, reply, ledgerTransactionId: null, clock.UtcNow, ct);

        return claimed ? reply : await ReadStoredReplyAsync(command, ct);
    }

    /// <summary>
    /// Saklanan cevabı aynen döner. Yeniden hesaplanmıyor: aradan geçen sürede bakiye
    /// değişmiş olabilir ve aynı komuta iki farklı cevap vermek saga'yı bozardı.
    /// </summary>
    private async Task<WithdrawalReply> ReadStoredReplyAsync(
        DebitForWithdrawal command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var stored = await db.ProcessedMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.MessageId == command.CommandId, ct);

        if (stored is null)
        {
            // Kapıya takıldık ama satır görünmüyor: diğer işlem henüz commit etmemiş
            // ya da geri almış. Geçici — mesaj kuyruğa geri konmalı.
            throw new InvalidOperationException(
                $"Komut {command.CommandId} sahiplenilemedi ama kaydı da yok. Yeniden denenmeli.");
        }

        logger.LogInformation(
            "Çekim komutu zaten işlenmiş, saklanan cevap dönülüyor. Saga {SagaId}", command.SagaId);

        return new WithdrawalReply(stored.ReplyRoutingKey, stored.ReplyPayload, Replayed: true);
    }

    private static async Task<LedgerAccount> LoadWalletAsync(
        WalletDbContext db, DebitForWithdrawal command, Currency currency, CancellationToken ct)
    {
        var wallet = await db.LedgerAccounts.FirstOrDefaultAsync(a => a.Id == command.WalletId, ct)
                     ?? throw new WithdrawalRejectedException($"Cüzdan yok: {command.WalletId}");

        if (wallet.Type is not LedgerAccountType.UserWallet)
        {
            throw new WithdrawalRejectedException(
                $"{wallet.Id} bir cüzdan değil, {wallet.Type} sistem hesabı.");
        }

        if (wallet.Currency != currency)
        {
            throw new WithdrawalRejectedException(
                $"Cüzdan {wallet.Currency} tutuyor, {currency} istendi.");
        }

        return wallet;
    }

    /// <summary>
    /// Çekim bankadan çıkıyor, o yüzden banka sağlayıcısının clearing'i. Sabit kimlik
    /// kullanılmıyor: clearing sağlayıcı BAŞINA ayrı (decisions.md madde 14).
    /// </summary>
    private static async Task<LedgerAccount> LoadClearingAsync(
        WalletDbContext db, Currency currency, CancellationToken ct)
    {
        return await db.LedgerAccounts
                   .FirstOrDefaultAsync(
                       a => a.Type == LedgerAccountType.Clearing
                            && a.Provider == SystemAccounts.BankFake
                            && a.Currency == currency,
                       ct)
               ?? throw new InvalidOperationException(
                   $"'{SystemAccounts.BankFake}' sağlayıcısının {currency} clearing hesabı yok.");
    }

    private static async Task<LedgerAccount> LoadRevenueAsync(
        WalletDbContext db, Currency currency, CancellationToken ct)
    {
        return await db.LedgerAccounts
                   .FirstOrDefaultAsync(
                       a => a.Type == LedgerAccountType.Revenue && a.Currency == currency, ct)
               ?? throw new InvalidOperationException($"{currency} revenue hesabı yok.");
    }
}

/// <summary>
/// Çekim iş kuralı gereği reddedildi. <see cref="DomainException"/> olması önemli:
/// akış bunu hata değil CEVAP olarak ele alıyor ve saga'ya bildiriyor.
/// </summary>
public sealed class WithdrawalRejectedException(string message) : DomainException(message);
