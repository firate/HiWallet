using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Persistence;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Baseline;

/// <summary>
/// Veritabanı çağrılarının zaman aşımı (baseline.md madde 11).
///
/// Npgsql'in varsayılanı 30 saniye; takılan bir sorgu bağlantıyı ve request'i o
/// kadar tutar. Ayar uygulamanın KENDİ kurulumundan okunuyor: kaydı unutmak
/// sessizce varsayılana düşmek demek ve bunu başka hiçbir test yakalamaz.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DbTimeoutTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Uygulamanin_DbContexti_KomutZamanAsimiTasir()
    {
        await using var factory = new WalletApiFactory(postgres);

        var contexts = factory.Services.GetRequiredService<IDbContextFactory<WalletDbContext>>();

        await using var db = await contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Database.GetCommandTimeout().ShouldBe(DbTimeouts.CommandSeconds);
    }

    /// <summary>
    /// Sınır gerçekten Postgres'e kadar gidiyor: sunucuda bekleyen bir sorgu
    /// süre dolunca kesiliyor. Bekleme değeri sınırın hemen üstünde seçildi ki
    /// test sınırdan fazla sürmesin.
    /// </summary>
    [Fact]
    public async Task SureyiAsanSorgu_Kesiliyor()
    {
        await using var factory = new WalletApiFactory(postgres);

        var contexts = factory.Services.GetRequiredService<IDbContextFactory<WalletDbContext>>();

        await using var db = await contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);

        // CommandTimeout bu tek çağrı için kısaltılıyor: gerçek değerle beklemek
        // testi on saniye uzatırdı ve ölçülen şey aynı.
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(1));

        await Should.ThrowAsync<Exception>(() =>
            db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5)", TestContext.Current.CancellationToken));
    }
}
