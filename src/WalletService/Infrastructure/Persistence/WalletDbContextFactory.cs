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
    public WalletDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__WalletOwner")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__WalletOwner ortamda yok. Kabuğa .env yükle: " +
                "set -a; . ./.env; set +a");

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new WalletDbContext(options);
    }
}
