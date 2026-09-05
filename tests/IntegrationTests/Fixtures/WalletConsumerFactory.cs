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
    /// <summary>%2 komisyon: 100 çekimde 2, cüzdandan 102 çıkıyor.</summary>
    public const decimal WithdrawalCommissionRate = 0.02m;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                // wallet-api ile AYNI şema: ikisi de aynı tabloları yazıyor,
                // ayrı deployable olmaları bunu değiştirmiyor.
                ["ConnectionStrings:Wallet"] = postgres.ConnectionString,

                // Çekim tarifesi appsettings.json'dan da okunabilirdi ama testin
                // beklediği sayılar o dosya değiştiğinde sessizce kaymasın diye
                // burada AÇIKÇA veriliyor.
                ["Withdrawals:Commission:Rate"] = WithdrawalCommissionRate.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["Withdrawals:Commission:Minimum"] = "0",
                ["Withdrawals:Limit:PerTransaction"] = "20000",
                ["Withdrawals:Limit:Daily"] = "50000"
            };

            BrokerSettings.ApplyFallbacks(overrides);

            config.AddInMemoryCollection(overrides);
        });
    }
}
