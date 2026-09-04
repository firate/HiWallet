using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.TopupWebhook.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Uygulama çalışırken devreye girmez.
/// </summary>
internal sealed class InboxDbContextFactory : IDesignTimeDbContextFactory<InboxDbContext>
{
    /// <summary>
    /// Bağlantı dizesi yoksa kullanılan yer tutucu. <c>migrations add</c> ve
    /// <c>migrations bundle</c> yalnızca MODELE bakıyor, hiçbir yere bağlanmıyor —
    /// docker build sırasında ortamda <c>.env</c> olmadığı için bunlar dizesiz de
    /// çalışabilmeli. <c>database update</c> gerçekten bağlanıyor; host adı bilerek
    /// açıklayıcı, hata mesajı tek başına neyin eksik olduğunu söylesin diye.
    /// </summary>
    private const string PlaceholderConnectionString =
        "Host=connection-string-not-configured;Database=hiwallet_topup;Username=topup_app";

    public InboxDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Topup")
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<InboxDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new InboxDbContext(options);
    }
}
