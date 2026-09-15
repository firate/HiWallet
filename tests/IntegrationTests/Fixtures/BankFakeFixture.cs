using HiWallet.Bank.Fake.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// SAHTE BANKANIN veritabanı. Üretimde böyle bir veritabanı yok — sahte banka da
/// yok. Testte <see cref="BankFixture"/>'dan AYRI bir schema: sınır korunuyor ve
/// adaptörün sahte bankanın senaryolarını göremediği yapısal olarak doğru kalıyor
/// (decisions.md madde 35).
///
/// Paylaşsalardı test yanlış bir şeyi kanıtlardı: adaptörün "senaryo ne diyormuş"
/// diye bakamadığını, ancak bakamayacak durumdayken kanıtlayabilirsin.
/// </summary>
public sealed class BankFakeFixture : IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "ConnectionStrings__IntegrationTests";

    private readonly string _adminConnectionString;

    public BankFakeFixture()
    {
        _adminConnectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionEnvironmentVariable} ortamda yok. Kabuğa .env yükle: " +
                "set -a; . ./.env; set +a");

        Schema = $"itbankfake_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            SearchPath = Schema,
            MaxPoolSize = 10
        }.ConnectionString;
    }

    public string Schema { get; }

    public string ConnectionString { get; }

    public async ValueTask InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE SCHEMA \"{Schema}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    public BankFakeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BankFakeDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
            .Options;

        return new BankFakeDbContext(options);
    }

    public IDbContextFactory<BankFakeDbContext> ContextFactory => new Factory(this);

    private sealed class Factory(BankFakeFixture fixture) : IDbContextFactory<BankFakeDbContext>
    {
        public BankFakeDbContext CreateDbContext() => fixture.CreateContext();
    }
}
