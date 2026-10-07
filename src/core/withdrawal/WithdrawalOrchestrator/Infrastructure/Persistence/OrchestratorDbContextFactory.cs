using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Uygulama çalışırken devreye girmez.
/// </summary>
internal sealed class OrchestratorDbContextFactory : IDesignTimeDbContextFactory<OrchestratorDbContext>
{
    /// <summary>
    /// Bağlantı dizesi yoksa kullanılan yer tutucu. <c>migrations add</c> ve
    /// <c>migrations bundle</c> yalnızca MODELE bakıyor, hiçbir yere bağlanmıyor —
    /// docker build sırasında ortamda <c>.env</c> olmadığı için bunlar dizesiz de
    /// çalışabilmeli. <c>database update</c> gerçekten bağlanıyor; host adı bilerek
    /// açıklayıcı, hata mesajı tek başına neyin eksik olduğunu söylesin diye.
    /// </summary>
    private const string PlaceholderConnectionString =
        "Host=connection-string-not-configured;Database=hiwallet_withdrawal;Username=withdrawal_app";

    public OrchestratorDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Withdrawal")
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrchestratorDbContext(options);
    }
}
