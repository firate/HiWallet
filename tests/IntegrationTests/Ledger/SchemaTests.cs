using HiWallet.WalletService.Domain.Ledger;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Ledger;

/// <summary>
/// Migration'ın ürettiği şemanın beklenen garantileri verdiğini doğrular.
/// Elle yazılmış DDL yok — fixture migration'ı uyguluyor, testler ona bakıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migration_SistemHesaplariniSeedEder()
    {
        await using var db = postgres.CreateContext();

        var system = await db.LedgerAccounts
            .Where(a => a.AccountId == null)
            .OrderBy(a => a.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        system.Count.ShouldBe(SystemAccounts.All.Count);
        system.Select(a => a.Id).ShouldBe(SystemAccounts.All.Select(a => a.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Migration_SistemHesaplarininBakiyeSatirlariniAcar()
    {
        // Tembel yaratılsaydı ilk ledger yazımı satırı bulamazdı; ayrıca mutabakat
        // sorgusu LEFT JOIN olduğu için sapmayı hiç göremezdi.
        var ct = TestContext.Current.CancellationToken;
        await using var db = postgres.CreateContext();

        var ids = SystemAccounts.All.Select(a => a.Id).ToArray();

        var balances = await db.LedgerBalances
            .Where(b => ids.Contains(b.LedgerAccountId))
            .ToListAsync(ct);

        // Hesap başına kova sayısı kadar satır (decisions.md madde 36): sistem
        // hesapları da her kovadan bakiye tutabiliyor ve biri eksik kalsaydı o
        // kovaya ilk yazma anında satır bulunamazdı.
        balances.Count.ShouldBe(SystemAccounts.All.Count * FundTypes.All.Count);

        // Seed bakiyesi sıfır. Koleksiyondaki diğer testler clearing'e yazıyor ve
        // sıfırı doğrudan beklemek bu testi sıraya bağlardı; seed sıfırsa bakiye
        // satırın entry toplamına eşit. Hiç yazılmamış satırın version'ı da seed'deki gibi 0.
        var entries = await db.LedgerEntries
            .Where(e => ids.Contains(e.LedgerAccountId))
            .GroupBy(e => new { e.LedgerAccountId, e.FundType })
            .Select(g => new { g.Key.LedgerAccountId, g.Key.FundType, Sum = g.Sum(e => e.Amount) })
            .ToListAsync(ct);

        foreach (var balance in balances)
        {
            var written = entries.SingleOrDefault(
                e => e.LedgerAccountId == balance.LedgerAccountId && e.FundType == balance.FundType);

            balance.Balance.ShouldBe(written?.Sum ?? 0m);

            if (written is null) balance.Version.ShouldBe(0);
        }

        foreach (var account in SystemAccounts.All)
        {
            balances
                .Where(b => b.LedgerAccountId == account.Id)
                .Select(b => b.FundType)
                .ShouldBe(FundTypes.All, ignoreOrder: true);
        }
    }

    [Fact]
    public async Task Migration_ZeroSumTriggerKurar()
    {
        await using var db = postgres.CreateContext();

        // pg_trigger veritabanı genelinde; regclass search_path üzerinden çözüldüğü
        // için sorgu bu koşunun schema'sındaki tabloya sabitleniyor.
        var count = await db.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                  FROM pg_trigger
                 WHERE tgname = 'trg_ledger_balanced'
                   AND tgrelid = 'ledger_entries'::regclass
                """)
            .SingleAsync(TestContext.Current.CancellationToken);

        count.ShouldBe(1);
    }

    [Fact]
    public async Task Migration_ProviderKeyHesaplananKolonUretir()
    {
        // ux_ledger_accounts_system bu kolonun üstünde; COALESCE(provider,'') ifadesi
        // EF modelinde kalabilsin diye STORED generated column'a çevrilmişti.
        await using var db = postgres.CreateContext();

        var revenueKey = await db.Database
            .SqlQuery<string>(
                $"""SELECT provider_key AS "Value" FROM ledger_accounts WHERE id = {SystemAccounts.RevenueTry}""")
            .SingleAsync(TestContext.Current.CancellationToken);

        // revenue'nun provider'ı NULL; kolon onu boş string'e normalize etmeli.
        revenueKey.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task SistemHesabi_AyniTipSaglayiciCurrency_IkinciKezAcilamaz()
    {
        await using var db = postgres.CreateContext();

        db.LedgerAccounts.Add(LedgerAccount.System(
            Guid.NewGuid(),
            LedgerAccountType.Clearing,
            SystemAccounts.StripeFake,
            SystemAccounts.DefaultCurrency,
            DateTimeOffset.UtcNow));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        ex.InnerException?.Message.ShouldContain("ux_ledger_accounts_system");
    }
}
