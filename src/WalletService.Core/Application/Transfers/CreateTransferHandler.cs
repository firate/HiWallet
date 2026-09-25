using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HiWallet.WalletService.Application.Transfers;

/// <summary>
/// Transfer çekirdeği: tek ACID transaction, optimistic lock, saga YOK (overview.md madde 4).
///
/// Akış sırası önemli ve <c>ledger-schema.md</c>'deki referans akıştan bir noktada
/// AYRILIYOR — idempotency kapısı policy'den önce. Gerekçe aşağıda.
/// </summary>
public sealed class CreateTransferHandler(
    IDbContextFactory<WalletDbContext> contextFactory,
    LimitPolicy limitPolicy,
    CommissionPolicy commissionPolicy,
    IClock clock,
    ILogger<CreateTransferHandler> logger)
{
    /// <summary>decisions.md madde 9: 3 deneme, her denemede yeniden oku.</summary>
    private const int MaxAttempts = 3;

    public async Task<TransferResult> HandleAsync(CreateTransferCommand command, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AttemptAsync(command, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Eski version ile tekrar denemek sonsuza kadar başarısız olur; retry'ın
                // anlamı YENİ anlık görüntüyle yeniden denemek. Taze context, taze okuma,
                // komisyon ve limit yeniden hesaplanır (decisions.md madde 9).
                logger.LogDebug(
                    "Transfer çakışması, yeniden deneniyor. Deneme {Attempt}/{Max}, cüzdan {WalletId}",
                    attempt, MaxAttempts, command.FromWalletId);
            }
        }
    }

    private async Task<TransferResult> AttemptAsync(CreateTransferCommand command, CancellationToken ct)
    {
        var currency = Currency.From(command.Currency);
        var amount = new Money(command.Amount, currency);

        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Transfer tutarı pozitif olmalı.", nameof(command));
        }

        if (command.FromWalletId == command.ToWalletId)
        {
            throw new ArgumentException("Gönderen ve alan cüzdan aynı olamaz.", nameof(command));
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var sender = await LoadWalletAsync(db, command.FromWalletId, currency, ct);
        var receiver = await LoadWalletAsync(db, command.ToWalletId, currency, ct);

        // --- Idempotency kapısı: policy'den ÖNCE ---------------------------------
        // ledger-schema.md'nin referans akışı bunu policy'den SONRA gösteriyor ama bu
        // bozuk: ilk transfer günlük limiti doldurduysa aynı request'in tekrarı limit
        // aşımına takılır ve orijinal işlemi dönmek yerine 422 verir. Tekrar eden request
        // hiçbir kuralı yeniden değerlendirmemeli, sadece olanı dönmeli.
        if (command.IdempotencyKey is { } key)
        {
            var existing = await db.LedgerTransactions
                .AsNoTracking()
                .Where(t => t.LedgerAccountId == sender.Id && t.IdempotencyKey == key)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);

            if (existing is { } existingId)
            {
                return new TransferResult(existingId, Replayed: true);
            }
        }

        // --- Policy ---------------------------------------------------------------
        var senderAccountId = sender.AccountId
                              ?? throw new InvalidOperationException(
                                  $"Cüzdan {sender.Id} bir hesaba bağlı değil.");
        var receiverAccountId = receiver.AccountId
                                ?? throw new InvalidOperationException(
                                    $"Cüzdan {receiver.Id} bir hesaba bağlı değil.");

        // Tip tarafların hesap tipleriyle uyuşmalı: kişiye Payment, işletmeye P2P
        // geçmez. person/business cüzdanda değil hesapta duruyor (decisions.md madde 20)
        // ve ona çekirdek değil policy bakıyor (madde 6). Kapıdan SONRA: tekrar eden
        // request bu kuralı da yeniden değerlendirmez (madde 21). İki cüzdan aynı
        // hesaba ait olabilir; o zaman sözlükte tek satır olur.
        var accountTypes = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == senderAccountId || a.Id == receiverAccountId)
            .ToDictionaryAsync(a => a.Id, a => a.Type, ct);

        var senderType = accountTypes[senderAccountId];
        var receiverType = accountTypes[receiverAccountId];

        if (!command.Type.Matches(senderType, receiverType))
        {
            throw new TransferTypeMismatchException(command.Type, senderType, receiverType);
        }

        var commission = commissionPolicy.Calculate(command.Type, amount);
        var debit = amount + commission;

        var spentToday = await SpentTodayAsync(db, senderAccountId, command.Type, currency, ct);

        // Kapsam hesap, cüzdan değil: aksi halde ikinci cüzdan açılarak aşılır
        // (decisions.md madde 20). Limit cüzdandan ÇIKAN toplama uygulanıyor, yani
        // komisyon dahil — koruduğu şey "bu hesaptan bugün ne kadar para çıktı".
        limitPolicy.Ensure(senderAccountId, command.Type, debit, spentToday);

        // --- Ledger ---------------------------------------------------------------
        var now = clock.UtcNow;
        // Aktör gönderen cüzdanın SAHİBİ hesap (decisions.md madde 34). Bugün bu
        // bilgi kimlik doğrulamadan değil, yüklenmiş cüzdandan türetiliyor — çünkü
        // authn henüz yok (baseline.md "Opsiyonel Katman A").
        //
        // Authn geldiğinde burası değişmeli: aktör komutla gelen DOĞRULANMIŞ özne
        // olmalı ve cüzdanın sahibiyle EŞLEŞTİĞİ kontrol edilmeli. Türetmeye devam
        // etmek, başkasının cüzdanından yapılan bir transferi o cüzdanın sahibi
        // yapmış gibi kaydederdi.
        var tx = LedgerTransaction.Create(
            Guid.NewGuid(), command.Type.ToLedgerType(), sender.Id,
            Actor.Customer(senderAccountId), now, command.IdempotencyKey);

        // Kovalara dağıtım (decisions.md madde 36). Sıra promo → card → cash: en
        // kısıtlı kova önce eriyor. Promo yalnızca Payment'ta ve yalnızca alıcı
        // işyerinde geçerli partiler kadar yer alıyor (madde 37). Yetersizlik burada
        // yakalanıyor ve cüzdanın TOPLAM bakiyesi yetse bile reddedilebiliyor —
        // müşterinin gördüğü toplam ile bu işlemde çıkabilen tutar ayrı şeyler.
        var senderBalances = await db.LedgerBalances
            .Where(b => b.LedgerAccountId == sender.Id)
            .ToDictionaryAsync(b => b.FundType, b => b.Money, ct);

        IReadOnlyList<PromoTake> promoTakes = [];
        IReadOnlyList<FundAllocation> allocations;

        if (command.Type is TransferType.Payment && receiver.AccountId is { } merchantAccountId)
        {
            var lots = await UsablePromoLotsAsync(db, sender.Id, merchantAccountId, now, ct);
            promoTakes = PromoLots.Take(lots, amount.Amount);

            allocations = FundAllocator.ForPayment(
                sender.Id, senderBalances, new Money(promoTakes.Sum(t => t.Amount), currency), amount, commission);
        }
        else
        {
            allocations = FundAllocator.ForTransfer(sender.Id, senderBalances, amount, commission);
        }

        // Kova tipi karşı tarafta AYNEN korunuyor: korunmasaydı kart kısıtı tek
        // adımda delinirdi (kartla yükle, ikinci hesabına gönder, oradan IBAN'a çek).
        // Tek istisna promo: işyeri gerçek bir satışın bedelini alıyor ve promo payı
        // ona cash olarak geçiyor (madde 37).
        foreach (var allocation in allocations)
        {
            tx.AddEntry(sender.Id, allocation.Debit.Negated, allocation.FundType);

            if (!allocation.Amount.IsZero)
            {
                var receivedAs = allocation.FundType is FundType.Promo ? FundType.Cash : allocation.FundType;
                tx.AddEntry(receiver.Id, allocation.Amount, receivedAs);
            }

            if (!allocation.Commission.IsZero)
            {
                // Komisyon ayrı bir transfer değil, aynı atomik işlemin ek bacağı.
                // Kovası da çıktığı yerle aynı; başka bir kovaya yazılsaydı o kovanın
                // bakiyesi karşılıksız artardı.
                tx.AddEntry(SystemAccounts.RevenueTry, allocation.Commission, allocation.FundType);
            }
        }

        // DB'deki deferred trigger'dan önce, daha anlaşılır hatayla.
        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // Hangi partiden ne kadar harcandığı. Gönderenin promo bakiye satırıyla aynı
        // transaction'da: o satırın version'ı aynı partiyi tüketen eşzamanlı ödemeleri
        // sıraya sokuyor (madde 37).
        foreach (var take in promoTakes)
        {
            db.PromoConsumptions.Add(new PromoConsumption(take.GrantId, tx.Id, take.Amount, now));
        }

        // --- Projeksiyon ----------------------------------------------------------
        // Bakiye asla ledger'a yazmadan güncellenmez (CLAUDE.md). Sıra ledger hesap
        // kimliğine göre ARTAN — A→B ve B→A eşzamanlı geldiğinde deadlock olmasın
        // (decisions.md madde 8).
        var deltas = tx.Entries
            .Select(e => (e.LedgerAccountId, e.Money, e.FundType))
            .OrderBy(x => x.LedgerAccountId)
            .ToArray();

        var affectedIds = deltas.Select(d => d.LedgerAccountId).ToArray();
        var affected = await db.LedgerAccounts
            .Where(a => affectedIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var (ledgerAccountId, delta, fundType) in deltas)
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(
                                  b => b.LedgerAccountId == ledgerAccountId && b.FundType == fundType, ct)
                          ?? throw new InvalidOperationException(
                              $"Bakiye satırı yok: {ledgerAccountId} / {fundType}");

            // Negatife düşebilirlik hesabın TİPİNDEN geliyor, transferdeki rolünden değil.
            // "Gönderen ve alan hariç herkes düşebilir" diye yazmak bugün doğru sonucu
            // verirdi ama kuralı yanlış yere bağlardı (decisions.md madde 6).
            balance.Apply(delta, affected[ledgerAccountId].CanGoNegative, now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new TransferResult(tx.Id, Replayed: false);
    }

    private static async Task<LedgerAccount> LoadWalletAsync(
        WalletDbContext db, Guid walletId, Currency currency, CancellationToken ct)
    {
        var wallet = await db.LedgerAccounts.FirstOrDefaultAsync(a => a.Id == walletId, ct)
                     ?? throw new WalletNotFoundException(walletId);

        if (wallet.Type is not LedgerAccountType.UserWallet)
        {
            throw new ArgumentException(
                $"{walletId} bir cüzdan değil, {wallet.Type} sistem hesabı.", nameof(walletId));
        }

        if (wallet.Currency != currency)
        {
            throw new ArgumentException(
                $"Cüzdan {walletId} {wallet.Currency} tutuyor, {currency} istendi.", nameof(currency));
        }

        return wallet;
    }

    /// <summary>
    /// Cüzdanın bu işyerinde geçerli, süresi dolmamış partileri ve kalanları.
    ///
    /// Yalnızca seçili işyerleriyle kısıtlı partiler okunuyor: her yerde geçerli
    /// parti platform fonlu ve <c>accepts_promo</c> işaretiyle birlikte geliyor
    /// (decisions.md madde 37); bugün o partiyi açan bir yol yok.
    /// </summary>
    private static async Task<List<PromoLot>> UsablePromoLotsAsync(
        WalletDbContext db, Guid walletId, Guid merchantAccountId, DateTimeOffset now, CancellationToken ct)
    {
        return await db.PromoGrants
            .Where(g => g.LedgerAccountId == walletId
                        && (g.ExpiresAt == null || g.ExpiresAt > now)
                        && g.Scope == PromoScope.SelectedBusinesses
                        && g.Merchants.Any(m => m.AccountId == merchantAccountId))
            .Select(g => new PromoLot(
                g.Id,
                g.Amount - (db.PromoConsumptions.Where(c => c.GrantId == g.Id).Sum(c => (decimal?)c.Amount) ?? 0m),
                g.ExpiresAt,
                true,
                g.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Hesabın TÜM cüzdanlarından bugün bu tiple çıkan toplam. Tek cüzdandan toplansaydı
    /// limit ikinci cüzdan açılarak aşılırdı (decisions.md madde 20).
    /// </summary>
    private async Task<Money> SpentTodayAsync(
        WalletDbContext db, Guid accountId, TransferType type, Currency currency, CancellationToken ct)
    {
        var since = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var ledgerType = type.ToLedgerType();

        var walletIds = db.LedgerAccounts
            .Where(a => a.AccountId == accountId)
            .Select(a => a.Id);

        // Debit bacaklarının toplamı; işaret negatif olduğu için sonuç ters çevriliyor.
        var debited = await db.LedgerEntries
            .Where(e => walletIds.Contains(e.LedgerAccountId)
                        && e.Amount < 0m
                        && e.Currency == currency
                        && db.LedgerTransactions.Any(t => t.Id == e.TransactionId && t.Type == ledgerType)
                        && e.CreatedAt >= since)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new Money(-debited, currency);
    }
}
