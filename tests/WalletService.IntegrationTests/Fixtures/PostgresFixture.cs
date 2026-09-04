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

        AppConnectionString = BuildAppConnectionString(_adminConnectionString, Schema);
    }

    public string Schema { get; }

    /// <summary>
    /// Bu koşunun schema'sına <c>wallet_owner</c> ile bağlanan connection string.
    /// Testlerin çoğu bunu kullanıyor — veri kurmak için DDL/DML yetkisi gerekiyor.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Aynı schema'ya UYGULAMA rolüyle (<c>wallet_app</c>) bağlanan connection string.
    ///
    /// Neden var: diğer her test <c>wallet_owner</c> ile bağlanıyor ve sahip rolüne
    /// <c>REVOKE</c> işlemediği için append-only kuralı o yoldan hiç sınanmıyordu.
    /// Yetki regresyonu ancak uygulamanın gerçekte kullandığı rolle yakalanır.
    ///
    /// Kimlik bilgisi ayrı bir ortam değişkeninden değil, uygulamanın kendi bağlantı
    /// dizesinden alınıyor; test DB'sinin host/veritabanı ile birleştiriliyor.
    /// </summary>
    public string AppConnectionString { get; }

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

    /// <summary>
    /// Uygulama rolünün kimliğini <c>ConnectionStrings:Wallet</c>'tan, hedef veritabanını
    /// test bağlantısından alır. Ayrı bir ortam değişkeni istememesi bilinçli: üçüncü bir
    /// parola daha dolaşıma sokmadan aynı rolü kullanıyor.
    /// </summary>
    private static string BuildAppConnectionString(string adminConnectionString, string schema)
    {
        var appCredentials = Environment.GetEnvironmentVariable("ConnectionStrings__Wallet")
                             ?? throw new InvalidOperationException(
                                 "ConnectionStrings__Wallet ortamda yok; uygulama rolüyle " +
                                 "koşan testler bu bağlantıdan kimlik alıyor.");

        var app = new NpgsqlConnectionStringBuilder(appCredentials);
        var target = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = app.Username,
            Password = app.Password,
            SearchPath = schema,
            MaxPoolSize = 5
        };

        return target.ConnectionString;
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
