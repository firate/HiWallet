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
        await using var db = postgres.CreateContext();

        var balances = await db.LedgerBalances
            .Where(b => SystemAccounts.All.Select(a => a.Id).Contains(b.LedgerAccountId))
            .ToListAsync(TestContext.Current.CancellationToken);

        balances.Count.ShouldBe(SystemAccounts.All.Count);
        balances.ShouldAllBe(b => b.Balance == 0m && b.Version == 0);
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
