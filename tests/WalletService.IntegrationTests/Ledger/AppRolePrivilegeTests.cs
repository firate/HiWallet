using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using HiWallet.WalletService.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HiWallet.WalletService.IntegrationTests.Ledger;

/// <summary>
/// UYGULAMA ROLÜYLE (<c>wallet_app</c>) koşan testler. Diğer her test
/// <c>wallet_owner</c> ile bağlanıyor ve sahip rolüne <c>REVOKE</c> işlemediği için
/// append-only kuralı o yoldan hiç sınanmıyor — bu dosya o boşluğu kapatıyor.
///
/// Rol yoksa (tek kullanıcılı local kurulum) testler atlanıyor: kurulumu zorunlu
/// kılmak yerine, varsa doğrulanıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AppRolePrivilegeTests(PostgresFixture postgres)
{
    private static async Task<bool> AppRoleUsableAsync(PostgresFixture postgres, CancellationToken ct)
    {
        try
        {
            await using var connection = new NpgsqlConnection(postgres.AppConnectionString);
            await connection.OpenAsync(ct);
            return true;
        }
        catch (Exception e) when (e is PostgresException or NpgsqlException or InvalidOperationException)
        {
            return false;
        }
    }

    [Fact]
    public async Task UygulamaRolu_LedgerEntriesGuncelleyemez()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await AppRoleUsableAsync(postgres, ct), "wallet_app rolü kurulu değil");

        // Güncellenecek bir satır olsun diye önce owner ile para koy.
        Guid wallet;
        await using (var owner = postgres.CreateContext())
        {
            var accountId = await LedgerSeeder.CreateAccountAsync(owner, AccountType.Person, ct);
            wallet = await LedgerSeeder.CreateWalletAsync(owner, accountId, "Yetki testi", ct);
            await LedgerSeeder.FundAsync(owner, wallet, 100m, ct);
        }

        await using var app = new NpgsqlConnection(postgres.AppConnectionString);
        await app.OpenAsync(ct);

        foreach (var sql in new[]
                 {
                     "UPDATE ledger_entries SET amount = amount + 1 WHERE id = (SELECT min(id) FROM ledger_entries)",
                     "DELETE FROM ledger_entries WHERE id = (SELECT min(id) FROM ledger_entries)"
                 })
        {
            await using var command = new NpgsqlCommand(sql, app);

            var ex = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(ct));

            // 42501 = insufficient_privilege. Başka bir sebeple reddedilmesi yeterli değil;
            // kuralı zorlayan şey YETKİ olmalı (decisions.md madde 5).
            ex.SqlState.ShouldBe("42501", $"REVOKE işlemiyor: {sql}");
        }
    }

    [Fact]
    public async Task UygulamaRolu_LedgerEntriesOkuyabilirVeYazabilir()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await AppRoleUsableAsync(postgres, ct), "wallet_app rolü kurulu değil");

        await using var app = new NpgsqlConnection(postgres.AppConnectionString);
        await app.OpenAsync(ct);

        // REVOKE yalnızca UPDATE/DELETE'i almalı; SELECT ve INSERT kalmalı, yoksa
        // uygulama hiç transfer yapamaz.
        await using var select = new NpgsqlCommand("SELECT count(*) FROM ledger_entries", app);
        await Should.NotThrowAsync(() => select.ExecuteScalarAsync(ct));

        // INSERT yetkisi var mı: geçersiz FK ile deneniyor, yetki sorunu olsaydı 42501,
        // yetki varsa 23503 (foreign_key_violation) alınır.
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO ledger_entries (transaction_id, ledger_account_id, amount, currency)
            VALUES ('00000000-0000-0000-0000-000000000000',
                    '00000000-0000-0000-0000-000000000000', 1, 'TRY')
            """, app);

        var ex = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(ct));
        ex.SqlState.ShouldBe("23503", "INSERT yetkisi alınmış görünüyor");
    }

    [Fact]
    public async Task UygulamaRolu_NormalTransferYapabilir()
    {
        // Yetkiler doğru daraltıldıysa uygulama işini yapabilmeli. Bu test olmasaydı
        // "her şeyi revoke et" de yeşil görünürdü.
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await AppRoleUsableAsync(postgres, ct), "wallet_app rolü kurulu değil");

        Guid from, to;
        await using (var owner = postgres.CreateContext())
        {
            var a1 = await LedgerSeeder.CreateAccountAsync(owner, AccountType.Person, ct);
            var a2 = await LedgerSeeder.CreateAccountAsync(owner, AccountType.Person, ct);
            from = await LedgerSeeder.CreateWalletAsync(owner, a1, "App gönderen", ct);
            to = await LedgerSeeder.CreateWalletAsync(owner, a2, "App alıcı", ct);
            await LedgerSeeder.FundAsync(owner, from, 500m, ct);
        }

        var handler = new CreateTransferHandler(
            new AppContextFactory(postgres.AppConnectionString),
            new LimitPolicy(new Dictionary<TransferType, TransferLimit>()),
            new CommissionPolicy(new Dictionary<TransferType, CommissionRate>()),
            new SystemClock(),
            NullLogger<CreateTransferHandler>.Instance);

        var result = await handler.HandleAsync(
            new CreateTransferCommand(from, to, 100m, "TRY", TransferType.P2P), ct);

        result.TransactionId.ShouldNotBe(Guid.Empty);

        await using var verify = postgres.CreateContext();
        var balance = await verify.LedgerBalances.SingleAsync(b => b.LedgerAccountId == from, ct);
        balance.Balance.ShouldBe(400m);
    }

    private sealed class AppContextFactory(string connectionString) : IDbContextFactory<WalletDbContext>
    {
        public WalletDbContext CreateDbContext()
        {
            return new WalletDbContext(new DbContextOptionsBuilder<WalletDbContext>()
                .UseNpgsql(connectionString)
                .Options);
        }
    }
}
