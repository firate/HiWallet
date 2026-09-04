using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.WalletService.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek Postgres. Mock DB yok (structure.md "Ayrım").
///
/// İzolasyon KOŞU BAŞINA SCHEMA ile sağlanıyor: her test koşusu kendi schema'sını açar,
/// migration'ı oraya uygular, sonunda düşürür. Testcontainers da aynı izolasyonu verirdi
/// ama bir Docker daemon'a konuşmak zorunda; bu yol Docker'sız çalışıyor ve paralel
/// koşuları da engellemiyor (schema adları farklı).
///
/// Bedeli: testler bir Postgres sunucusuna erişim istiyor, offline çalışmıyor.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "ConnectionStrings__IntegrationTests";

    private readonly string _adminConnectionString;

    public PostgresFixture()
    {
        _adminConnectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionEnvironmentVariable} ortamda yok. Kabuğa .env yükle: " +
                "set -a; . ./.env; set +a");

        // Schema adı koşuya özel. Paralel koşular ve yarıda kalmış eski schema'lar
        // birbirine karışmasın diye zaman damgası + rastgele ek.
        Schema = $"it_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            SearchPath = Schema,

            // Concurrency testi yüzlerce transfer'i aynı anda başlatıyor. Havuz sınırsız
            // olsaydı her biri kendi bağlantısını açmaya çalışır ve sunucunun slot'ları
            // tükenirdi (53300) — bu testin ölçmek istediği şey değil. Sınırlı havuz
            // gerçek servisin davranışı: fazla istek bağlantı bekler, hata almaz.
            MaxPoolSize = 20
        }.ConnectionString;
    }

    public string Schema { get; }

    /// <summary>Bu koşunun schema'sına bağlanan connection string.</summary>
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

        // Şema migration'dan geliyor, elle DDL yok — tek kaynak migration.
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

    public WalletDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                // Migration geçmişi de bu koşunun schema'sında dursun, public'e sızmasın.
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
            .Options;

        return new WalletDbContext(options);
    }

    /// <summary>
    /// Handler her retry denemesinde TAZE context ister — eski deneme başarısız olduğunda
    /// change tracker'da bayat entity'ler kalıyor ve yeniden okunması gerekiyor
    /// (decisions.md madde 9).
    /// </summary>
    public IDbContextFactory<WalletDbContext> ContextFactory => new Factory(this);

    private sealed class Factory(PostgresFixture fixture) : IDbContextFactory<WalletDbContext>
    {
        public WalletDbContext CreateDbContext()
        {
            return fixture.CreateContext();
        }
    }
}

/// <summary>
/// Tüm integration testler tek schema paylaşır — koşu başına bir kurulum yeterli,
/// test başına schema açmak koşuyu gereksiz yavaşlatır. Testler kendi verilerini
/// kendileri temizler.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
