using HiWallet.WalletConsumer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Tüketici host'u. Ayrı bir deployable olduğu için testte de ayrı ayağa
/// kaldırılıyor — wallet-api ile aynı süreçte koşsaydı test, üretimde olmayan bir
/// yakınlığı doğrulamış olurdu.
/// </summary>
public sealed class WalletConsumerFactory(PostgresFixture postgres)
    : WebApplicationFactory<WalletConsumerApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                // wallet-api ile AYNI şema: ikisi de aynı tabloları yazıyor,
                // ayrı deployable olmaları bunu değiştirmiyor.
                ["ConnectionStrings:Wallet"] = postgres.ConnectionString
            };

            BrokerSettings.ApplyFallbacks(overrides);

            config.AddInMemoryCollection(overrides);
        });
    }
}
