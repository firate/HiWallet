using HiWallet.BankWebhook;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Bankanın bizi çağırdığı endpoint. Adaptörle AYNI veritabanına bakıyor ama AYRI host —
/// canlıdaki ayrımın aynısı (decisions.md madde 28, 35).
/// </summary>
/// <param name="rateLimitBurst">
/// Verilmezse limit testin kendi request'lerini boğmayacak kadar yüksek. Verilirse
/// kova bu kadar request alıyor ve dakikada bir token yenileniyor.
/// </param>
public sealed class BankWebhookFactory(
    BankFixture bank, string? callbackSecret = null, int? rateLimitBurst = null)
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
                    callbackSecret ?? TestBankSecrets.CallbackSecret,
                ["RateLimiting:BankCallbacks:BurstSize"] = (rateLimitBurst ?? 10_000).ToString(),
                ["RateLimiting:BankCallbacks:SustainedPerMinute"] = rateLimitBurst is null ? "10000" : "1"
            }));
    }
}
