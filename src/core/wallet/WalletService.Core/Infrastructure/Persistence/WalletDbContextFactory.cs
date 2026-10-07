using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Uygulama çalışırken devreye girmez.
///
/// Migration'lar <c>wallet_owner</c> ile koşar, uygulama <c>wallet_app</c> ile bağlanır —
/// ayrım şart, çünkü <c>ledger_entries</c> üzerindeki REVOKE yalnızca tablo sahibi OLMAYAN
/// bir role işler. Bu yüzden burada <c>ConnectionStrings__WalletOwner</c> okunuyor,
/// uygulamanın kullandığı bağlantı değil.
/// </summary>
internal sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    /// <summary>
    /// Bağlantı dizesi yoksa kullanılan yer tutucu.
    ///
    /// <c>migrations add</c> ve <c>migrations bundle</c> yalnızca MODELE bakıyor, hiçbir
    /// veritabanına bağlanmıyor — bunlar bağlantı dizesi olmadan da çalışmalı. Docker
    /// imajı build edilirken ortamda <c>.env</c> yok ve bundle üretimi burada patlıyordu.
    ///
    /// <c>database update</c> ise gerçekten bağlanıyor; bağlantı verilmemişse bu host'a
    /// bağlanmaya çalışıp hata veriyor. Host adı bilerek açıklayıcı seçildi, hata mesajı
    /// tek başına neyin eksik olduğunu söylesin diye.
    /// </summary>
    private const string PlaceholderConnectionString =
        "Host=connection-string-not-configured;Database=hiwallet_wallet;Username=wallet_owner";

    public WalletDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__WalletOwner")
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new WalletDbContext(options);
    }
}
