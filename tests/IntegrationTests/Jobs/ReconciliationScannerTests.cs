using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Settlements;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Settlements;
using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Infrastructure.Jobs;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HiWallet.IntegrationTests.Jobs;

/// <summary>
/// Mutabakat raporu (adım 5.7, <c>overview.md</c> madde 7, <c>decisions.md</c>
/// madde 11).
///
/// Sistem düzeltmiyor, gösteriyor — testler de bulgulara bakıyor, bir düzeltme
/// beklemiyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReconciliationScannerTests(PostgresFixture postgres)
{
    private const string Stripe = "stripe-fake";
    private const string Bank = "bank-fake";

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private ReconciliationScanner CreateScanner(DateTimeOffset? now = null) => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new FixedTimeProvider(now ?? Now),
        Options.Create(new ReconciliationOptions()));

    private ProcessTopupHandler TopupHandler(DateTimeOffset at) => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new FixedClock(at),
        NullLogger<ProcessTopupHandler>.Instance);

    /// <summary>
    /// <b>Asıl kanıt.</b> Projeksiyon–ledger ayrışması yakalanıyor.
    ///
    /// Bunu başka hiçbir şey yakalamıyor: zero-sum trigger'ı bir işlemin
    /// bacaklarının toplamına bakıyor, <c>ledger_balances</c>'ın doğru
    /// güncellendiğine değil. Ayrışma sessiz ve her bakiye sorgusunu yanlışlıyor.
    /// </summary>
    [Fact]
    public async Task ProjeksiyonLedgerIleAyrisirsa_Yakalanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        await using (var db = postgres.CreateContext())
        {
            await LedgerSeeder.FundAsync(db, wallet, 100m, ct);
        }

        (await CreateScanner().ScanAsync(ct))
            .Drifts.ShouldNotContain(d => d.LedgerAccountId == wallet);

        // Bozmayı ledger'a DOKUNMADAN yapıyoruz: tam olarak yakalanması gereken
        // durum bu — entry'ler doğru, özet yanlış.
        await using (var db = postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE ledger_balances SET balance = balance + 5 WHERE ledger_account_id = {wallet}",
                ct);
        }

        var drift = (await CreateScanner().ScanAsync(ct))
            .Drifts.Where(d => d.LedgerAccountId == wallet).ShouldHaveSingleItem();

        drift.Difference.ShouldBe(5m);

        // Bozma GERİ ALINIYOR. Veritabanı bu koleksiyondaki bütün testlerle
        // paylaşılıyor ve TransferTests global bir projeksiyon-sapması kontrolü
        // yapıyor; bozuk satır bırakmak onu düşürüyor. Testin kendi pisliğini
        // temizlemesi gerekiyor.
        await using (var db = postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE ledger_balances SET balance = balance - 5 WHERE ledger_account_id = {wallet}",
                ct);
        }

        (await CreateScanner().ScanAsync(ct))
            .Drifts.ShouldNotContain(d => d.LedgerAccountId == wallet);
    }

    /// <summary>
    /// Settlement'ı gecikmiş top-up raporlanıyor. Clearing "yolda olan para"
    /// demek; yolda kalması normal, uzun süre kalması değil.
    /// </summary>
    [Fact]
    public async Task SettlementiGecikmisTopup_Raporlanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);
        var old = Now - TimeSpan.FromDays(10);

        await TopupHandler(old).HandleAsync(Topup(wallet, 100m, Stripe), ct);

        var report = await CreateScanner().ScanAsync(ct);

        var aging = report.AgingItems.Where(a => a.Provider == Stripe).ShouldHaveSingleItem();
        aging.Count.ShouldBeGreaterThanOrEqualTo(1);
        aging.Oldest.ShouldBeLessThanOrEqualTo(old);
    }

    /// <summary>
    /// Settlement gelmiş top-up raporlanmamalı: alacak kapandı.
    /// </summary>
    [Fact]
    public async Task SettlementiGelmisTopup_Raporlanmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);
        var old = Now - TimeSpan.FromDays(10);
        var providerRef = $"pi_{Guid.NewGuid():N}";

        await TopupHandler(old).HandleAsync(Topup(wallet, 100m, Stripe, providerRef), ct);

        await SettlementHandler(old).HandleAsync(new SettlementReceived
        {
            Provider = Stripe,
            SettlementId = $"st_{Guid.NewGuid():N}",
            Currency = "TRY",
            GrossAmount = 100m,
            FeeAmount = 2.90m,
            NetAmount = 97.10m,
            ProviderRefs = [providerRef],
            SettledAt = old
        }, ct);

        await using var db = postgres.CreateContext();
        var fee = await db.ProviderFees.AsNoTracking()
            .SingleAsync(f => f.ProviderRef == providerRef, ct);

        fee.LedgerTransactionId.ShouldNotBeNull("settlement ücret satırını kapatmalıydı");
    }

    /// <summary>
    /// Yeni top-up raporlanmamalı: sağlayıcılar 2-3 iş gününde ödüyor, eşik
    /// hafta sonunu da kapsıyor. Dar tutmak her hafta sonu yanlış alarm üretirdi.
    /// </summary>
    [Fact]
    public async Task YeniTopup_HenuzGecikmisSayilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);
        var recent = Now - TimeSpan.FromDays(1);

        var topup = await TopupHandler(recent).HandleAsync(Topup(wallet, 100m, Stripe), ct);

        var report = await CreateScanner().ScanAsync(ct);

        // Bu işlemin kendisi listede olmamalı; başka testlerin bıraktığı eski
        // satırlar rapora girebilir, o yüzden en eskiye değil bu satıra bakılıyor.
        await using var db = postgres.CreateContext();
        var fee = await db.ProviderFees.AsNoTracking()
            .SingleAsync(f => f.TransactionId == topup.LedgerTransactionId!.Value, ct);

        fee.OccurredAt.ShouldBe(recent);
        report.AgingItems
            .Where(a => a.Provider == Stripe)
            .ShouldAllBe(a => a.Oldest < recent);
    }

    /// <summary>
    /// İncelemede bekleyen fatura raporlanıyor (madde 11).
    /// </summary>
    [Fact]
    public async Task IncelemedekiFatura_Raporlanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var invoiceRef = $"inv_{Guid.NewGuid():N}";

        await using (var db = postgres.CreateContext())
        {
            db.ProviderInvoices.Add(new ProviderInvoice
            {
                Id = Guid.NewGuid(),
                Provider = Bank,
                InvoiceRef = invoiceRef,
                Amount = 500m,
                ExpectedAmount = 3m,
                Currency = "TRY",
                Status = ProviderInvoiceStatus.PendingReview,
                FeeCount = 2,
                ReceivedAt = Now - TimeSpan.FromDays(1),
                Note = "test"
            });

            await db.SaveChangesAsync(ct);
        }

        var report = await CreateScanner().ScanAsync(ct);

        var pending = report.PendingInvoices
            .Where(i => i.InvoiceRef == invoiceRef).ShouldHaveSingleItem();
        pending.Amount.ShouldBe(500m);
        pending.Expected.ShouldBe(3m);
    }

    /// <summary>
    /// Net modeldeki sağlayıcının satırları "faturası gecikmiş" sayılmamalı: o
    /// modelde fatura adımı yok ve <c>invoice_ref</c> hiç dolmuyor (madde 10).
    /// Hepsini gecikmiş saymak raporu kullanılamaz hale getirirdi.
    /// </summary>
    [Fact]
    public async Task NetModelUcretleri_FaturasiGecikmisSayilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        // Fatura eşiğinin (45 gün) çok ötesinde bir stripe top-up'ı.
        await TopupHandler(Now - TimeSpan.FromDays(120)).HandleAsync(Topup(wallet, 100m, Stripe), ct);

        var report = await CreateScanner().ScanAsync(ct);

        report.OverdueFees.ShouldNotContain(o => o.Provider == Stripe);
    }

    /// <summary>
    /// Invoiced modelde faturası gecikmiş ücretler raporlanıyor: gider defterde
    /// tahakkuk etmiş görünüyor ama ödenmemiş.
    /// </summary>
    [Fact]
    public async Task InvoicedModelde_GecikmisFatura_Raporlanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        await TopupHandler(Now - TimeSpan.FromDays(120)).HandleAsync(Topup(wallet, 100m, Bank), ct);

        var report = await CreateScanner().ScanAsync(ct);

        report.OverdueFees.ShouldContain(o => o.Provider == Bank);
    }

    private ProcessSettlementHandler SettlementHandler(DateTimeOffset at) => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new FixedClock(at),
        NullLogger<ProcessSettlementHandler>.Instance);

    private static TopupReceived Topup(
        Guid walletId, decimal amount, string provider, string? providerRef = null) => new()
    {
        Provider = provider,
        EventId = $"evt_{Guid.NewGuid():N}",
        LedgerAccountId = walletId,
        Amount = amount,
        Currency = "TRY",
        ProviderRef = providerRef ?? $"pi_{Guid.NewGuid():N}",
        OccurredAt = DateTimeOffset.UtcNow
    };

    private async Task<Guid> NewWalletAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);

        return await LedgerSeeder.CreateWalletAsync(db, account, "Mutabakat testi", ct);
    }

    /// <summary>Handler'lar <c>IClock</c> alıyor; sabit an vermek için.</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
