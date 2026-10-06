using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.CardTopup.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Bağlantı dizesi yoksa yer tutucu:
/// <c>migrations add</c> ve <c>bundle</c> yalnızca modele bakıyor (orchestrator'daki gerekçe).
/// </summary>
internal sealed class CardTopupDbContextFactory : IDesignTimeDbContextFactory<CardTopupDbContext>
{
    private const string PlaceholderConnectionString =
        "Host=connection-string-not-configured;Database=hiwallet_card_topup;Username=card_topup_app";

    public CardTopupDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__CardTopup") ?? PlaceholderConnectionString;

        return new CardTopupDbContext(new DbContextOptionsBuilder<CardTopupDbContext>()
            .UseNpgsql(connectionString)
            .Options);
    }
}
