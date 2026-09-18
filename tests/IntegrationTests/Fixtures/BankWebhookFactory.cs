using HiWallet.BankWebhook;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Bankanın bizi çağırdığı uç. Adaptörle AYNI veritabanına bakıyor ama AYRI host —
/// canlıdaki ayrımın aynısı (decisions.md madde 28, 35).
/// </summary>
public sealed class BankWebhookFactory(BankFixture bank, string? callbackSecret = null)
    : WebApplicationFactory<BankWebhookApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Bank"] = bank.ConnectionString,
                [$"Banks:{TestBankSecrets.Bank}:CallbackSecret"] =
                    callbackSecret ?? TestBankSecrets.CallbackSecret
            }));
    }
}
