using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Settlements;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletService.Application.Settlements;
using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Domain.Promos;
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
                $"""
                UPDATE ledger_balances SET balance = balance + 5
                 WHERE ledger_account_id = {wallet} AND fund_type = 'cash'
                """,
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
                $"""
                UPDATE ledger_balances SET balance = balance - 5
                 WHERE ledger_account_id = {wallet} AND fund_type = 'cash'
                """,
                ct);
        }

        (await CreateScanner().ScanAsync(ct))
            .Drifts.Where(d => d.LedgerAccountId == wallet).ShouldBeEmpty();
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

    /// <summary>
    /// Cüzdanın promo bakiyesi partilerin kalanlarının toplamına eşit olmalı
    /// (decisions.md madde 37). Ledger'a partisiz yazılmış promo tam olarak bu
    /// ayrışma: bakiye ledger ile tutuyor, partiler tutmuyor.
    /// </summary>
    [Fact]
    public async Task PromoBakiyesiPartilerleAyrisirsa_Yakalanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        Guid merchant;
        await using (var db = postgres.CreateContext())
        {
            var business = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            merchant = await LedgerSeeder.CreateWalletAsync(db, business, "Mutabakat işyeri", ct);
            await LedgerSeeder.FundAsync(db, merchant, 100m, ct);
        }

        await new GrantPromoHandler(postgres.ContextFactory, new FixedClock(Now), NullLogger<GrantPromoHandler>.Instance)
            .HandleAsync(new GrantPromoCommand(merchant, wallet, 40m, "TRY", null, Guid.NewGuid().ToString("N")), ct);

        (await CreateScanner().ScanAsync(ct))
            .PromoDrifts.ShouldNotContain(d => d.LedgerAccountId == wallet);

        await using (var db = postgres.CreateContext())
        {
            await LedgerSeeder.FundAsync(db, wallet, 5m, ct, FundType.Promo);
        }

        var drift = (await CreateScanner().ScanAsync(ct))
            .PromoDrifts.Where(d => d.LedgerAccountId == wallet).ShouldHaveSingleItem();

        drift.Balance.ShouldBe(45m);
        drift.FromGrants.ShouldBe(40m);

        // Ayrışma geri alınıyor: veritabanı koleksiyondaki bütün testlerle paylaşılıyor.
        await using (var db = postgres.CreateContext())
        {
            await LedgerSeeder.FundAsync(db, wallet, -5m, ct, FundType.Promo);
        }

        (await CreateScanner().ScanAsync(ct))
            .PromoDrifts.ShouldNotContain(d => d.LedgerAccountId == wallet);
    }

    /// <summary>
    /// Platform fonlu promo harcandığında işyerine e-para yazılıyor, koruma hesabına para
    /// girmiyor (decisions.md madde 37). Açık, fonlanana kadar Warning olarak raporlanıyor.
    /// </summary>
    [Fact]
    public async Task PlatformPromosuHarcaninca_KorumaHesabiAcigiRaporlanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await NewWalletAsync(ct);
        var currency = SystemAccounts.DefaultCurrency;

        Guid shop, merchant;
        await using (var db = postgres.CreateContext())
        {
            shop = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            merchant = await LedgerSeeder.CreateWalletAsync(db, shop, "Açık işyeri", ct);
            await db.Database.ExecuteSqlAsync($"UPDATE accounts SET accepts_promo = true WHERE id = {shop}", ct);

            var campaign = PromoCampaign.Create(
                Guid.NewGuid(), "Açık testi", PromoCampaignRule.DailyPaymentTotal, 1_000_000m,
                PromoRewardType.Fixed, 1m, null, null, currency, PromoScope.AllBusinesses, null,
                1_000m, 1_000m, 1_000m, Now.AddYears(-1), Now.AddYears(-1).AddDays(1), [], [], Now.AddYears(-1));
            db.PromoCampaigns.Add(campaign);

            var tx = LedgerTransaction
                .Create(Guid.NewGuid(), LedgerTransactionType.PromoGrant, customer, SystemActors.PromoCampaign,
                    Now, Guid.NewGuid().ToString("N"))
                .AddEntry(SystemAccounts.PromoExpenseTry, new Money(-30m, currency), FundType.Promo)
                .AddEntry(customer, new Money(30m, currency), FundType.Promo);
            db.LedgerTransactions.Add(tx);
            db.PromoGrants.Add(PromoGrant.FromCampaign(Guid.NewGuid(), customer, new Money(30m, currency), campaign, tx.Id, Now));

            foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
            {
                var balance = await db.LedgerBalances.SingleAsync(
                    b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == FundType.Promo, ct);
                balance.Apply(entry.Money, canGoNegative: entry.LedgerAccountId != customer, Now);
            }

            await db.SaveChangesAsync(ct);
        }

        var before = GapOf(await CreateScanner().ScanAsync(ct));

        await new CreateTransferHandler(
                postgres.ContextFactory,
                new LimitPolicy(new Dictionary<TransferType, TransferLimit>()),
                new CommissionPolicy(new Dictionary<TransferType, CommissionRate>()),
                new FixedClock(Now),
                NullLogger<CreateTransferHandler>.Instance)
            .HandleAsync(new CreateTransferCommand(
                customer, merchant, 20m, "TRY", TransferType.Payment, Guid.NewGuid().ToString("N")), ct);

        GapOf(await CreateScanner().ScanAsync(ct)).ShouldBe(before + 20m);
    }

    private static decimal GapOf(ReconciliationReport report) =>
        report.PromoFundingGaps.Where(g => g.Currency == "TRY").Sum(g => g.Amount);

    private async Task<Guid> NewWalletAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);

        return await LedgerSeeder.CreateWalletAsync(db, account, "Mutabakat testi", ct);
    }
}
