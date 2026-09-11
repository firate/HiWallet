using System.Text.Json.Nodes;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// Saga'nın wallet tarafı: düşme ve iade. Broker gerekmiyor — komut doğrudan
/// handler'a veriliyor. Sınanan şey taşıma değil, LEDGER ETKİSİ.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WithdrawalCommandTests(PostgresFixture postgres)
{
    private static readonly Currency Try = SystemAccounts.DefaultCurrency;

    /// <summary>%2 komisyon, en az 2 TRY. 100 çekim → 2 komisyon, 102 düşülür.</summary>
    private static WithdrawalPolicy Policy(decimal rate = 0.02m, decimal? perTransaction = null) =>
        new(new CommissionRate(rate, Minimum: rate > 0 ? 2m : null),
            new TransferLimit(perTransaction, Daily: 50_000m));

    private DebitForWithdrawalHandler Debit(WithdrawalPolicy? policy = null) => new(
        postgres.ContextFactory,
        policy ?? Policy(),
        new SystemClock(),
        NullLogger<DebitForWithdrawalHandler>.Instance);

    private RefundWithdrawalHandler Refund() => new(
        postgres.ContextFactory,
        new SystemClock(),
        NullLogger<RefundWithdrawalHandler>.Instance);

    // --- Düşme ---------------------------------------------------------------

    [Fact]
    public async Task Dusme_UcBacakYazar_ToplamSifir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(1_000m, ct);

        var reply = await Debit().HandleAsync(DebitCommand(walletId, 100m), ct);

        reply.Replayed.ShouldBeFalse();
        reply.RoutingKey.ShouldBe(nameof(WithdrawalDebited));

        await using var db = postgres.CreateContext();

        var tx = await db.LedgerTransactions
            .Include(t => t.Entries)
            .SingleAsync(t => t.LedgerAccountId == walletId
                              && t.Type == LedgerTransactionType.Withdrawal, ct);

        tx.Entries.Count.ShouldBe(3);
        tx.Entries.Sum(e => e.Amount).ShouldBe(0m);

        tx.Entries.Single(e => e.LedgerAccountId == walletId).Amount.ShouldBe(-102m);
        tx.Entries.Single(e => e.LedgerAccountId == SystemAccounts.ClearingBankTry).Amount.ShouldBe(100m);
        tx.Entries.Single(e => e.LedgerAccountId == SystemAccounts.RevenueTry).Amount.ShouldBe(2m);

        (await BalanceAsync(db, walletId, ct)).ShouldBe(898m);
    }

    [Fact]
    public async Task Dusme_KomisyonSifirsa_IkiBacak()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(1_000m, ct);

        await Debit(Policy(rate: 0m)).HandleAsync(DebitCommand(walletId, 100m), ct);

        await using var db = postgres.CreateContext();

        var tx = await db.LedgerTransactions
            .Include(t => t.Entries)
            .SingleAsync(t => t.LedgerAccountId == walletId
                              && t.Type == LedgerTransactionType.Withdrawal, ct);

        // Sıfır tutarlı revenue bacağı ledger'a gürültü yazmaktan başka bir şey yapmazdı.
        tx.Entries.Count.ShouldBe(2);
        tx.Entries.Sum(e => e.Amount).ShouldBe(0m);
        tx.Entries.ShouldNotContain(e => e.LedgerAccountId == SystemAccounts.RevenueTry);
    }

    /// <summary>
    /// Reddetme hata değil cevap: ledger'a hiçbir şey yazılmıyor ama saga cevabını
    /// alıyor. Dead-letter'a gitseydi müşteri sonsuza kadar "işleniyor" görürdü.
    /// </summary>
    [Fact]
    public async Task YetersizBakiye_Reddedilir_LedgeraYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(50m, ct);

        var reply = await Debit().HandleAsync(DebitCommand(walletId, 100m), ct);

        reply.RoutingKey.ShouldBe(nameof(WithdrawalDebitRejected));

        await using var db = postgres.CreateContext();

        (await db.LedgerTransactions.AnyAsync(
            t => t.LedgerAccountId == walletId && t.Type == LedgerTransactionType.Withdrawal, ct))
            .ShouldBeFalse();

        (await BalanceAsync(db, walletId, ct)).ShouldBe(50m);
    }

    [Fact]
    public async Task LimitAsimi_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(10_000m, ct);

        var reply = await Debit(Policy(perTransaction: 500m))
            .HandleAsync(DebitCommand(walletId, 1_000m), ct);

        reply.RoutingKey.ShouldBe(nameof(WithdrawalDebitRejected));
        reply.Payload.ShouldContain("Withdrawal.PerTransaction");
    }

    /// <summary>
    /// Limit komisyon DAHİL toplama uygulanıyor (decisions.md madde 22). 500 tutar +
    /// 10 komisyon = 510 ve tavan 505: yalnız istenen tutara bakılsaydı geçerdi.
    /// </summary>
    [Fact]
    public async Task Limit_KomisyonDahilToplamaUygulanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(10_000m, ct);

        var reply = await Debit(Policy(perTransaction: 505m))
            .HandleAsync(DebitCommand(walletId, 500m), ct);

        reply.RoutingKey.ShouldBe(nameof(WithdrawalDebitRejected));
    }

    [Fact]
    public async Task AyniKomut_IkinciKez_LedgeraDokunmaz_AyniCevabiDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(1_000m, ct);
        var command = DebitCommand(walletId, 100m);

        var first = await Debit().HandleAsync(command, ct);
        var second = await Debit().HandleAsync(command, ct);

        second.Replayed.ShouldBeTrue();
        second.RoutingKey.ShouldBe(first.RoutingKey);

        // ANLAMCA aynı: jsonb kolonu anahtar sırasını normalize ediyor, o yüzden
        // karşılaştırma ayrıştırılmış hal üzerinden.
        SameJson(second.Payload, first.Payload).ShouldBeTrue();

        await using var db = postgres.CreateContext();

        (await db.LedgerTransactions.CountAsync(
            t => t.LedgerAccountId == walletId && t.Type == LedgerTransactionType.Withdrawal, ct))
            .ShouldBe(1);

        (await BalanceAsync(db, walletId, ct)).ShouldBe(898m);
    }

    /// <summary>
    /// Reddedilmiş komut tekrar gelirse kural YENİDEN DEĞERLENDİRİLMEMELİ. Aradan
    /// geçen sürede cüzdana para girdiyse ikinci teslim para düşerdi — oysa saga
    /// çoktan "reddedildi" diye kapanmış olurdu ve o para asla bankaya gitmezdi.
    /// </summary>
    [Fact]
    public async Task Reddedilen_KomutTekrarGelirse_BakiyeYetseBileReddedilmisKalir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(50m, ct);
        var command = DebitCommand(walletId, 100m);

        var first = await Debit().HandleAsync(command, ct);
        first.RoutingKey.ShouldBe(nameof(WithdrawalDebitRejected));

        // Bu arada cüzdana para giriyor.
        await using (var funding = postgres.CreateContext())
        {
            await LedgerSeeder.FundAsync(funding, walletId, 5_000m, ct);
        }

        var second = await Debit().HandleAsync(command, ct);

        second.Replayed.ShouldBeTrue();
        second.RoutingKey.ShouldBe(nameof(WithdrawalDebitRejected));

        await using var db = postgres.CreateContext();

        (await db.LedgerTransactions.AnyAsync(
            t => t.LedgerAccountId == walletId && t.Type == LedgerTransactionType.Withdrawal, ct))
            .ShouldBeFalse();
    }

    /// <summary>
    /// İade edilmiş çekim günlük limiti tüketmemeli: o para hesaptan çıkmadı.
    /// Transfer tarafındaki sayımdan farkı bu.
    /// </summary>
    [Fact]
    public async Task GunlukLimit_IadeEdilenCekimiSaymaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(100_000m, ct);

        // Günlük tavan 30.000; ilk çekim 25.000 (+500 komisyon) alıyor.
        var policy = new WithdrawalPolicy(
            new CommissionRate(0.02m), new TransferLimit(Daily: 30_000m));

        var firstSaga = Guid.NewGuid();
        await Debit(policy).HandleAsync(DebitCommand(walletId, 25_000m, firstSaga), ct);

        // İade edilmeden ikinci 25.000 limite takılır.
        var blocked = await Debit(policy).HandleAsync(DebitCommand(walletId, 25_000m), ct);
        blocked.RoutingKey.ShouldBe(nameof(WithdrawalDebitRejected));

        // İadeden sonra aynı istek geçmeli.
        await Refund().HandleAsync(new RefundWithdrawal
        {
            CommandId = Guid.NewGuid(),
            SagaId = firstSaga
        }, ct);

        var allowed = await Debit(policy).HandleAsync(DebitCommand(walletId, 25_000m), ct);

        allowed.RoutingKey.ShouldBe(nameof(WithdrawalDebited));
    }

    // --- İade -----------------------------------------------------------------

    /// <summary>
    /// <b>4.8'in en kritik testi.</b> Ters kayıt üç bacağın TAMAMINI geri almalı.
    /// <c>revenue</c> bacağı atlansaydı kayıt yine dengeli olurdu (cüzdan +102,
    /// clearing -100 → toplam +2 ≠ 0, ama komisyon kadar eksik bir cüzdan iadesiyle
    /// dengelenebilirdi) ve müşteri gerçekleşmemiş işlemin komisyonunu ödemiş kalırdı.
    /// </summary>
    [Fact]
    public async Task Iade_UcBacaginTumunuGeriAlir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(1_000m, ct);
        var sagaId = Guid.NewGuid();

        var revenueBefore = await SystemBalanceAsync(SystemAccounts.RevenueTry, ct);
        var clearingBefore = await SystemBalanceAsync(SystemAccounts.ClearingBankTry, ct);

        await Debit().HandleAsync(DebitCommand(walletId, 100m, sagaId), ct);

        var reply = await Refund().HandleAsync(
            new RefundWithdrawal { CommandId = Guid.NewGuid(), SagaId = sagaId }, ct);

        reply.RoutingKey.ShouldBe(nameof(WithdrawalRefunded));

        await using var db = postgres.CreateContext();

        var refund = await db.LedgerTransactions
            .Include(t => t.Entries)
            .SingleAsync(t => t.CorrelationId == sagaId && t.Type == LedgerTransactionType.Refund, ct);

        refund.Entries.Count.ShouldBe(3);
        refund.Entries.Sum(e => e.Amount).ShouldBe(0m);

        refund.Entries.Single(e => e.LedgerAccountId == walletId).Amount.ShouldBe(102m);
        refund.Entries.Single(e => e.LedgerAccountId == SystemAccounts.ClearingBankTry).Amount.ShouldBe(-100m);

        // Komisyon KOŞULSUZ iade (CLAUDE.md "Sağlayıcı ücretleri").
        refund.Entries.Single(e => e.LedgerAccountId == SystemAccounts.RevenueTry).Amount.ShouldBe(-2m);

        // Üç hesap da başladığı yere döndü.
        (await BalanceAsync(db, walletId, ct)).ShouldBe(1_000m);
        (await SystemBalanceAsync(SystemAccounts.RevenueTry, ct)).ShouldBe(revenueBefore);
        (await SystemBalanceAsync(SystemAccounts.ClearingBankTry, ct)).ShouldBe(clearingBefore);
    }

    [Fact]
    public async Task Iade_OrijinalKayitSilinmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(1_000m, ct);
        var sagaId = Guid.NewGuid();

        await Debit().HandleAsync(DebitCommand(walletId, 100m, sagaId), ct);
        await Refund().HandleAsync(
            new RefundWithdrawal { CommandId = Guid.NewGuid(), SagaId = sagaId }, ct);

        await using var db = postgres.CreateContext();

        // Ledger append-only: düzeltme ters kayıtla, silmeyle değil.
        (await db.LedgerTransactions.CountAsync(t => t.CorrelationId == sagaId, ct)).ShouldBe(2);
    }

    [Fact]
    public async Task AyniIadeKomutu_IkinciKez_IkinciTersKayitYazmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(1_000m, ct);
        var sagaId = Guid.NewGuid();

        await Debit().HandleAsync(DebitCommand(walletId, 100m, sagaId), ct);

        var command = new RefundWithdrawal { CommandId = Guid.NewGuid(), SagaId = sagaId };

        var first = await Refund().HandleAsync(command, ct);
        var second = await Refund().HandleAsync(command, ct);

        second.Replayed.ShouldBeTrue();
        SameJson(second.Payload, first.Payload).ShouldBeTrue();

        await using var db = postgres.CreateContext();

        (await db.LedgerTransactions.CountAsync(
            t => t.CorrelationId == sagaId && t.Type == LedgerTransactionType.Refund, ct))
            .ShouldBe(1);

        // Çift iade cüzdana ikinci kez para koyardı.
        (await BalanceAsync(db, walletId, ct)).ShouldBe(1_000m);
    }

    /// <summary>
    /// Düşme yoksa iade edilecek bir şey de yok. Bu bir iş kuralı reddi DEĞİL,
    /// tutarsızlık: saga'ya "iade edildi" denmemeli, mesaj dead-letter'a gitmeli.
    /// </summary>
    [Fact]
    public async Task Iade_OrijinalYoksa_Patlar()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Refund().HandleAsync(
                new RefundWithdrawal { CommandId = Guid.NewGuid(), SagaId = Guid.NewGuid() }, ct));
    }

    // --- yardımcılar ------------------------------------------------------------

    /// <summary>
    /// <c>jsonb</c> anahtar sırasını ve boşlukları normalize ediyor; saklanan cevap
    /// ilk yayınlananla anlamca aynı ama karakter karakter değil.
    /// </summary>
    private static bool SameJson(string left, string right) =>
        JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));

    /// <summary>
    /// Aynı saga'nın iki kaydı FARKLI aktör taşıyor (<c>decisions.md</c> madde 34).
    ///
    /// Düşmeyi müşteri başlattı — araya kuyruk girmesi bunu değiştirmiyor. İadeyi ise
    /// kimse istemedi: banka reddetti, saga karar verdi. Aktör "kaydı hangi taşıma
    /// getirdi" sorusunun değil, "kim başlattı" sorusunun cevabı.
    ///
    /// İkisine de <c>system</c> yazmak kolay olurdu ve hiçbir test kırılmazdı — o
    /// yüzden bu ayrımın kendi testi var.
    /// </summary>
    [Fact]
    public async Task Dusme_MusteriAktoru_Iade_SistemAktoru()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewFundedWalletAsync(1_000m, ct);
        var sagaId = Guid.NewGuid();

        await Debit().HandleAsync(DebitCommand(walletId, 100m, sagaId), ct);
        await Refund().HandleAsync(
            new RefundWithdrawal { CommandId = Guid.NewGuid(), SagaId = sagaId }, ct);

        await using var db = postgres.CreateContext();

        var accountId = await db.LedgerAccounts.AsNoTracking()
            .Where(a => a.Id == walletId)
            .Select(a => a.AccountId)
            .SingleAsync(ct);

        var debit = await db.LedgerTransactions.AsNoTracking()
            .SingleAsync(t => t.CorrelationId == sagaId && t.Type == LedgerTransactionType.Withdrawal, ct);

        debit.ActorType.ShouldBe(ActorType.Customer);
        debit.ActorId.ShouldBe(accountId!.Value.ToString(),
            "aktör cüzdanın değil hesabın kimliğini taşır (madde 20)");

        var refund = await db.LedgerTransactions.AsNoTracking()
            .SingleAsync(t => t.CorrelationId == sagaId && t.Type == LedgerTransactionType.Refund, ct);

        refund.ActorType.ShouldBe(ActorType.System);
        refund.ActorId.ShouldBe("withdrawal-saga");
    }

    private static DebitForWithdrawal DebitCommand(Guid walletId, decimal amount, Guid? sagaId = null) =>
        new()
        {
            CommandId = Guid.NewGuid(),
            SagaId = sagaId ?? Guid.NewGuid(),
            WalletId = walletId,
            Amount = amount,
            Currency = Try.Code
        };

    private async Task<Guid> NewFundedWalletAsync(decimal amount, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var walletId = await LedgerSeeder.CreateWalletAsync(db, accountId, "çekim", ct);

        await LedgerSeeder.FundAsync(db, walletId, amount, ct);

        return walletId;
    }

    private static async Task<decimal> BalanceAsync(
        WalletDbContext db, Guid ledgerAccountId, CancellationToken ct) =>
        (await db.LedgerBalances.AsNoTracking()
            .SingleAsync(b => b.LedgerAccountId == ledgerAccountId, ct)).Balance;

    private async Task<decimal> SystemBalanceAsync(Guid ledgerAccountId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return await BalanceAsync(db, ledgerAccountId, ct);
    }
}
