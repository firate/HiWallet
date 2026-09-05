using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.BankService.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Uygulama çalışırken devreye girmez.
/// </summary>
internal sealed class BankDbContextFactory : IDesignTimeDbContextFactory<BankDbContext>
{
    /// <summary>
    /// Bağlantı dizesi yoksa kullanılan yer tutucu. <c>migrations add</c> yalnızca
    /// MODELE bakıyor, hiçbir yere bağlanmıyor — docker build sırasında ortamda
    /// <c>.env</c> olmadığı için dizesiz de çalışabilmeli. Host adı bilerek
    /// açıklayıcı, hata mesajı tek başına neyin eksik olduğunu söylesin diye.
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
