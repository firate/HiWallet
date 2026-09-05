using HiWallet.BankService;
using HiWallet.BankService.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Sahte banka host'u. Ayrı deployable olduğu için testte de ayrı ayağa kaldırılıyor;
/// zincir testinde orchestrator ile arasında yalnızca broker var.
/// </summary>
/// <param name="defaultOutcome">
/// Senaryosu kurulmamış transferlerin sonucu. Zincir testinde yarışı kaldırıyor:
/// saga kimliği ancak çekim isteği kabul edildikten sonra öğrenilebiliyor, yani
/// senaryoyu API'den kurmak teorik olarak banka komutuna geç kalabilir. Varsayılanı
/// servis başlamadan vermek bu pencereyi tamamen kapatıyor.
/// </param>
public sealed class BankServiceFactory(BankFixture bank, TransferOutcome? defaultOutcome = null)
    : WebApplicationFactory<BankServiceApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Bank"] = bank.ConnectionString
            };

            if (defaultOutcome is { } outcome)
            {
                overrides["Bank:DefaultOutcome"] = outcome.ToString();
                overrides["Bank:DefaultTransientFailures"] = "2";
            }

            BrokerSettings.ApplyFallbacks(overrides);

            config.AddInMemoryCollection(overrides);
        });
    }
}
