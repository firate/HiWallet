using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.Bank.Fake.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Uygulama çalışırken devreye girmez.
/// </summary>
internal sealed class BankFakeDbContextFactory : IDesignTimeDbContextFactory<BankFakeDbContext>
{
    /// <summary>
    /// Bağlantı dizesi yoksa kullanılan yer tutucu. <c>migrations add</c> yalnızca
    /// MODELE bakıyor, hiçbir yere bağlanmıyor — docker build sırasında ortamda
    /// <c>.env</c> olmadığı için dizesiz de çalışabilmeli.
    /// </summary>
    private const string PlaceholderConnectionString =
        "Host=connection-string-not-configured;Database=hiwallet_bank_fake;Username=bank_fake_app";

    public BankFakeDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__BankFake")
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<BankFakeDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BankFakeDbContext(options);
    }
}
