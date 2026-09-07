using HiWallet.WalletConsumer;
using HiWallet.WalletService.Domain.Policies;
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
                ["Withdrawals:Limit:Daily"] = "50000",

                // Sağlayıcı tarifeleri, aynı gerekçeyle burada: appsettings.json
                // değiştiğinde testin beklediği ücret sessizce kaymasın.
                // Değerler TestProviders ile aynı — provider_fees satırını
                // doğrulayan testler ikisini birden kullanıyor.
                ["Providers:stripe-fake:FeeSettlement"] = nameof(FeeSettlement.Net),
                ["Providers:stripe-fake:Fee:Rate"] = Invariant(TestProviders.StripeRate),
                ["Providers:stripe-fake:Fee:Fixed"] = Invariant(TestProviders.StripeFixed),
                ["Providers:bank-fake:FeeSettlement"] = nameof(FeeSettlement.Invoiced),
                ["Providers:bank-fake:Fee:Rate"] = "0",
                ["Providers:bank-fake:Fee:Fixed"] = Invariant(TestProviders.BankFixed)
            };

            BrokerSettings.ApplyFallbacks(overrides);

            config.AddInMemoryCollection(overrides);
        });
    }

    private static string Invariant(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
