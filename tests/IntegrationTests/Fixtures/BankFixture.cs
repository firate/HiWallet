using HiWallet.BankService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// bank-service'in veritabanı. Üretimde ayrı bir veritabanı (<c>hiwallet_bank</c>);
/// testte aynı sunucuda AYRI BİR SCHEMA — sınır korunuyor ama koşu üçüncü bir
/// veritabanı kurmayı gerektirmiyor. <see cref="InboxFixture"/> ile aynı kalıp.
/// </summary>
public sealed class BankFixture : IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "ConnectionStrings__IntegrationTests";

    private readonly string _adminConnectionString;

    public BankFixture()
    {
        _adminConnectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionEnvironmentVariable} ortamda yok. Kabuğa .env yükle: " +
                "set -a; . ./.env; set +a");

        Schema = $"itbank_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";

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

    public BankDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
            .Options;

        return new BankDbContext(options);
    }

    public IDbContextFactory<BankDbContext> ContextFactory => new Factory(this);

    private sealed class Factory(BankFixture fixture) : IDbContextFactory<BankDbContext>
    {
        public BankDbContext CreateDbContext() => fixture.CreateContext();
    }
}
