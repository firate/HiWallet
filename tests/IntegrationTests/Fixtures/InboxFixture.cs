using HiWallet.TopupWebhook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// topup-webhook'un veritabanı. Üretimde ayrı bir veritabanı (<c>hiwallet_topup</c>);
/// testte aynı sunucuda AYRI BİR SCHEMA — sınır korunuyor (iki context birbirinin
/// tablosunu görmüyor) ama koşu ikinci bir veritabanı kurmayı gerektirmiyor.
/// </summary>
public sealed class InboxFixture : IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "ConnectionStrings__IntegrationTests";

    private readonly string _adminConnectionString;

    public InboxFixture()
    {
        _adminConnectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionEnvironmentVariable} ortamda yok. Kabuğa .env yükle: " +
                "set -a; . ./.env; set +a");

        Schema = $"itbox_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";

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

    public InboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InboxDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
            .Options;

        return new InboxDbContext(options);
    }

    public IDbContextFactory<InboxDbContext> ContextFactory => new Factory(this);

    private sealed class Factory(InboxFixture fixture) : IDbContextFactory<InboxDbContext>
    {
        public InboxDbContext CreateDbContext() => fixture.CreateContext();
    }
}
