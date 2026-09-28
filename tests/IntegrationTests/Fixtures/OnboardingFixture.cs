using HiWallet.Onboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// onboarding'in veritabanı. Canlıda ayrı bir Postgres sunucusu; testte aynı sunucuda
/// AYRI BİR SCHEMA. <see cref="OrchestratorFixture"/> ile aynı kalıp.
/// </summary>
public sealed class OnboardingFixture : IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "ConnectionStrings__IntegrationTests";

    private readonly string _adminConnectionString;

    public OnboardingFixture()
    {
        _adminConnectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionEnvironmentVariable} ortamda yok. Kabuğa .env yükle: " +
                "set -a; . ./.env; set +a");

        Schema = $"itonb_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";

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

    public OnboardingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
            .Options;

        return new OnboardingDbContext(options);
    }
}
