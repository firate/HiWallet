using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Jobs;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Jobs;

/// <summary>
/// İşletme günlük özeti (adım 5.3, <c>overview.md</c> madde 7).
///
/// Raporun tanımı docs'ta yok; buradaki testler <see cref="BusinessDailySummary"/>
/// yorumundaki varsayımı sabitliyor — ciro işletmenin TAHSİLATI, komisyon ise o
/// tahsilatların ürettiği platform geliri (parayı gönderen ödüyor).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BusinessSummaryCalculatorTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day = new(2026, 3, 15);
    private static readonly DateTimeOffset DayStart = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CalculatedAt = new(2026, 3, 16, 1, 0, 0, TimeSpan.Zero);

    private BusinessSummaryCalculator CreateCalculator() =>
        new(postgres.ContextFactory, new FixedTimeProvider(CalculatedAt));

    [Fact]
    public async Task Tahsilat_CiroyaVeKomisyonaYazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (business, wallet) = await NewBusinessAsync(ct);

        // İki ödeme: 200 ve 300, %2 komisyon karşılıkları 4 ve 6.
        await PaymentAsync(wallet, 200m, 4m, DayStart.AddHours(9), ct);
        await PaymentAsync(wallet, 300m, 6m, DayStart.AddHours(14), ct);

        await CreateCalculator().CalculateAsync(Day, ct);

        var summary = await ReadAsync(business, ct);

        summary.ShouldNotBeNull();
        summary.Volume.ShouldBe(500m);
        summary.TransactionCount.ShouldBe(2);
        summary.Commission.ShouldBe(10m);
        summary.CalculatedAt.ShouldBe(CalculatedAt);
    }

    /// <summary>
    /// Top-up ciro DEĞİL: işletmenin kendi para yüklemesi satış değil. Sayılsaydı
    /// ciro, işletme cüzdanına para koydukça şişerdi.
    /// </summary>
    [Fact]
    public async Task Topup_CiroyaSayilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (business, wallet) = await NewBusinessAsync(ct);

        await using (var db = postgres.CreateContext())
        {
            await LedgerSeeder.FundAsync(db, wallet, 1_000m, ct);
        }

        await CreateCalculator().CalculateAsync(Day, ct);

        (await ReadAsync(business, ct)).ShouldBeNull();
    }

    /// <summary>
    /// Başka güne düşen tahsilat bu günün özetine girmemeli.
    /// </summary>
    [Fact]
    public async Task BaskaGunundekiTahsilat_Sayilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (business, wallet) = await NewBusinessAsync(ct);

        await PaymentAsync(wallet, 100m, 2m, DayStart.AddDays(-1).AddHours(23), ct);
        await PaymentAsync(wallet, 100m, 2m, DayStart.AddDays(1), ct);
        await PaymentAsync(wallet, 50m, 1m, DayStart.AddHours(12), ct);

        await CreateCalculator().CalculateAsync(Day, ct);

        var summary = await ReadAsync(business, ct);

        summary.ShouldNotBeNull();
        summary.Volume.ShouldBe(50m);
        summary.TransactionCount.ShouldBe(1);
    }

    /// <summary>
    /// Person hesabı raporlanmıyor: rapor işletmeler için.
    /// </summary>
    [Fact]
    public async Task PersonHesabi_Raporlanmaz()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid person;
        Guid wallet;

        await using (var db = postgres.CreateContext())
        {
            person = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            wallet = await LedgerSeeder.CreateWalletAsync(db, person, "Kişi", ct);
        }

        await PaymentAsync(wallet, 100m, 2m, DayStart.AddHours(10), ct);

        await CreateCalculator().CalculateAsync(Day, ct);

        (await ReadAsync(person, ct)).ShouldBeNull();
    }

    /// <summary>
    /// Asıl kanıt: aynı gün yeniden hesaplandığında ikinci satır değil, aynı satırın
    /// güncellenmesi. Upsert olmasaydı her tur raporu ikiye katlardı — ve bu, hiçbir
    /// hata üretmeden, yalnızca sayılara bakınca fark edilirdi.
    /// </summary>
    [Fact]
    public async Task AyniGun_YenidenHesaplandigindaUzerineYazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (business, wallet) = await NewBusinessAsync(ct);

        await PaymentAsync(wallet, 100m, 2m, DayStart.AddHours(10), ct);
        await CreateCalculator().CalculateAsync(Day, ct);

        await PaymentAsync(wallet, 400m, 8m, DayStart.AddHours(11), ct);
        await CreateCalculator().CalculateAsync(Day, ct);

        await using var db = postgres.CreateContext();
        var rows = await db.BusinessDailySummaries
            .Where(s => s.AccountId == business && s.Day == Day)
            .ToListAsync(ct);

        rows.Count.ShouldBe(1, "aynı gün için ikinci satır yazılmamalı");
        rows[0].Volume.ShouldBe(500m);
        rows[0].TransactionCount.ShouldBe(2);
        rows[0].Commission.ShouldBe(10m);
    }

    private async Task<(Guid Business, Guid Wallet)> NewBusinessAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var business = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, business, "Dükkan", ct);

        return (business, wallet);
    }

    /// <summary>
    /// Ödeme kaydının ledger karşılığı: gönderen −(tutar+komisyon), alan +tutar,
    /// revenue +komisyon (overview.md madde 4). Bakiye projeksiyonu güncellenmiyor;
    /// rapor <c>ledger_entries</c>'ten hesaplanıyor, bakiyeden değil.
    /// </summary>
    private async Task PaymentAsync(
        Guid businessWallet, decimal amount, decimal commission, DateTimeOffset at, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var payerAccount = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var payer = await LedgerSeeder.CreateWalletAsync(db, payerAccount, "Ödeyen", ct);

        var currency = SystemAccounts.DefaultCurrency;

        var tx = LedgerTransaction
            .Create(Guid.NewGuid(), TransferType.Payment.ToLedgerType(), payer, at)
            .AddEntry(payer, new Money(-(amount + commission), currency))
            .AddEntry(businessWallet, new Money(amount, currency))
            .AddEntry(SystemAccounts.RevenueTry, new Money(commission, currency));

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        await db.SaveChangesAsync(ct);
    }

    private async Task<BusinessDailySummary?> ReadAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return await db.BusinessDailySummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AccountId == accountId && s.Day == Day, ct);
    }
}
