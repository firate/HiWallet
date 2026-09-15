using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.BankIntegration.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Uygulama çalışırken devreye girmez.
///
/// Migration'lar BU KÜTÜPHANEDE, host'larda değil: şemanın tek sahibi var. İki
/// host ayrı ayrı migration üretebilseydi hangisinin önce koştuğu şemayı belirlerdi.
/// </summary>
internal sealed class BankDbContextFactory : IDesignTimeDbContextFactory<BankDbContext>
{
    /// <summary>
    /// Bağlantı dizesi yoksa kullanılan yer tutucu. <c>migrations add</c> yalnızca
    /// MODELE bakıyor, hiçbir yere bağlanmıyor — docker build sırasında ortamda
    /// <c>.env</c> olmadığı için dizesiz de çalışabilmeli.
    /// </summary>
    private const string PlaceholderConnectionString =
        "Host=connection-string-not-configured;Database=hiwallet_bank;Username=bank_app";

    public BankDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Bank")
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BankDbContext(options);
    }
}
