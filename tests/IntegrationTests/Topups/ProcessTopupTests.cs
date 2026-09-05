using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Tüketici tarafı: <c>processed_events</c> + ledger tek transaction
/// (overview.md madde 5). Broker gerekmiyor — mesajın kendisi doğrudan handler'a
/// veriliyor. Burada sınanan şey taşıma değil, ledger etkisi ve idempotency.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProcessTopupTests(PostgresFixture postgres)
{
    private static readonly Currency Try = SystemAccounts.DefaultCurrency;

    private ProcessTopupHandler Handler() => new(
        postgres.ContextFactory,
        new SystemClock(),
        NullLogger<ProcessTopupHandler>.Instance);

    [Fact]
    public async Task Topup_CuzdaniArtirir_ClearingiEksiltir_ToplamSifir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);

        var result = await Handler().HandleAsync(Message(walletId, 250.00m), ct);

        result.Replayed.ShouldBeFalse();
        result.LedgerTransactionId.ShouldNotBeNull();

        await using var db = postgres.CreateContext();

        var entries = await db.LedgerEntries
            .Where(e => e.TransactionId == result.LedgerTransactionId)
            .ToListAsync(ct);

        entries.Count.ShouldBe(2);
        entries.Sum(e => e.Amount).ShouldBe(0m);

        entries.Single(e => e.LedgerAccountId == walletId).Amount.ShouldBe(250.00m);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.ClearingStripeTry)
            .Amount.ShouldBe(-250.00m);

        // Bakiye projeksiyonu ledger'la aynı yönde hareket etmiş olmalı.
        var balance = await db.LedgerBalances.SingleAsync(b => b.LedgerAccountId == walletId, ct);
        balance.Balance.ShouldBe(250.00m);
    }

    [Fact]
    public async Task Topup_LedgerTransactionTipiTopup_VeIdempotencyKeyEventId()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);
        var message = Message(walletId, 40.00m);

        var result = await Handler().HandleAsync(message, ct);

        await using var db = postgres.CreateContext();
        var tx = await db.LedgerTransactions.SingleAsync(t => t.Id == result.LedgerTransactionId, ct);

        tx.Type.ShouldBe(LedgerTransactionType.Topup);

        // Idempotency kapsamı ALICI cüzdan (decisions.md madde 15). Key sağlayıcıyla
        // birlikte: event_id yalnızca sağlayıcı içinde tekil, ledger'daki unique index
        // ise sağlayıcıyı tanımıyor.
        tx.LedgerAccountId.ShouldBe(walletId);
        tx.IdempotencyKey.ShouldBe($"{message.Provider}:{message.EventId}");
    }

    [Fact]
    public async Task AyniMesaj_IkiKez_BakiyeBirKezArtar()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);
        var message = Message(walletId, 75.50m);

        var first = await Handler().HandleAsync(message, ct);
        var second = await Handler().HandleAsync(message, ct);

        second.Replayed.ShouldBeTrue();

        // İkinci çağrı ORİJİNAL işlemin kimliğini dönüyor, yeni bir tane değil.
        second.LedgerTransactionId.ShouldBe(first.LedgerTransactionId);

        await using var db = postgres.CreateContext();

        var balance = await db.LedgerBalances.SingleAsync(b => b.LedgerAccountId == walletId, ct);
        balance.Balance.ShouldBe(75.50m);

        (await db.LedgerTransactions.CountAsync(
            t => t.LedgerAccountId == walletId && t.Type == LedgerTransactionType.Topup, ct))
            .ShouldBe(1);
    }

    [Fact]
    public async Task AyniMesaj_EsZamanliIkiTeslim_TekKezIslenir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);
        var message = Message(walletId, 10.00m);

        // Broker en az bir kez teslim ediyor ve çok instance'ta aynı mesaj aynı
        // anda iki tüketiciye düşebiliyor. Kapı "önce SELECT sonra INSERT" olsaydı
        // ikisi de "işlenmemiş" görüp ikisi de yazardı.
        var results = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(async _ =>
            {
                try
                {
                    return await Handler().HandleAsync(message, ct);
                }
                catch (DbUpdateException)
                {
                    // ledger_transactions üzerindeki unique index ikinci emniyet
                    // kemeri; kapıya aynı anda giren ikinci yazar buradan döner.
                    return new ProcessTopupResult(null, Replayed: true);
                }
            }));

        results.Count(r => !r.Replayed).ShouldBe(1, "yalnızca bir teslim ledger'a yazmalı");

        await using var db = postgres.CreateContext();

        var balance = await db.LedgerBalances.SingleAsync(b => b.LedgerAccountId == walletId, ct);
        balance.Balance.ShouldBe(10.00m);
    }

    [Fact]
    public async Task BilinmeyenCuzdan_KaliciHata()
    {
        var ct = TestContext.Current.CancellationToken;

        var exception = await Should.ThrowAsync<TopupRejectedException>(
            () => Handler().HandleAsync(Message(Guid.NewGuid(), 10m), ct));

        // Kalıcı: yeniden denemek aynı sonucu verir, mesaj dead-letter'a gitmeli.
        exception.Message.ShouldContain("Cüzdan yok");
    }

    [Fact]
    public async Task SistemHesabinaTopup_KaliciHata()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<TopupRejectedException>(
            () => Handler().HandleAsync(Message(SystemAccounts.RevenueTry, 10m), ct));
    }

    [Fact]
    public async Task ParaBirimiUyusmazligi_KaliciHata()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);

        var message = Message(walletId, 10m) with { Currency = "USD" };

        await Should.ThrowAsync<TopupRejectedException>(() => Handler().HandleAsync(message, ct));
    }

    [Fact]
    public async Task NegatifVeSifirTutar_KaliciHata()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);

        await Should.ThrowAsync<TopupRejectedException>(
            () => Handler().HandleAsync(Message(walletId, 0m), ct));

        await Should.ThrowAsync<TopupRejectedException>(
            () => Handler().HandleAsync(Message(walletId, -5m), ct));
    }

    [Fact]
    public async Task TaninmayanSaglayici_KaliciHata()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);

        // Clearing hesabı sağlayıcı başına; olmayan sağlayıcının clearing'i de yok.
        var message = Message(walletId, 10m) with { Provider = "hic-tanimli-olmayan" };

        var exception = await Should.ThrowAsync<TopupRejectedException>(
            () => Handler().HandleAsync(message, ct));

        exception.Message.ShouldContain("clearing");
    }

    [Fact]
    public async Task ReddedilenMesaj_LedgeraHicDokunmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);
        var message = Message(walletId, 10m) with { Currency = "USD" };

        await Should.ThrowAsync<TopupRejectedException>(() => Handler().HandleAsync(message, ct));

        await using var db = postgres.CreateContext();

        // processed_events'e de yazılmamalı: mesaj dead-letter'a gidiyor ve orada
        // incelenecek. "İşlendi" işaretlemek onu görünmez kılardı.
        (await db.ProcessedEvents.AnyAsync(e => e.EventId == message.EventId, ct)).ShouldBeFalse();

        var balance = await db.LedgerBalances.SingleAsync(b => b.LedgerAccountId == walletId, ct);
        balance.Balance.ShouldBe(0m);
    }

    [Fact]
    public async Task FarkliSaglayicilar_AyniEventId_AyriAyriIslenir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await NewWalletAsync(ct);
        var eventId = $"evt_{Guid.NewGuid():N}";

        var fromStripe = Message(walletId, 20m) with { EventId = eventId, Provider = SystemAccounts.StripeFake };
        var fromBank = Message(walletId, 30m) with { EventId = eventId, Provider = SystemAccounts.BankFake };

        var first = await Handler().HandleAsync(fromStripe, ct);
        var second = await Handler().HandleAsync(fromBank, ct);

        // event_id tek başına PK olsaydı ikinci sağlayıcının mesajı sessizce
        // yutulurdu. Tekillik (provider, event_id) üzerinde.
        first.Replayed.ShouldBeFalse();
        second.Replayed.ShouldBeFalse();

        await using var db = postgres.CreateContext();
        var balance = await db.LedgerBalances.SingleAsync(b => b.LedgerAccountId == walletId, ct);
        balance.Balance.ShouldBe(50m);
    }

    private static TopupReceived Message(Guid walletId, decimal amount) => new()
    {
        Provider = SystemAccounts.StripeFake,
        EventId = $"evt_{Guid.NewGuid():N}",
        LedgerAccountId = walletId,
        Amount = amount,
        Currency = Try.Code,
        ProviderRef = "pi_test",
        OccurredAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero)
    };

    private async Task<Guid> NewWalletAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);

        return await LedgerSeeder.CreateWalletAsync(db, accountId, "Top-up testi", ct);
    }
}
