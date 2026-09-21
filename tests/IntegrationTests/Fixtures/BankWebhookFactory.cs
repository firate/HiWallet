using HiWallet.BankWebhook;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Bankanın bizi çağırdığı endpoint. Adaptörle AYNI veritabanına bakıyor ama AYRI host —
/// canlıdaki ayrımın aynısı (decisions.md madde 28, 35).
/// </summary>
/// <param name="callbackSecrets">
/// Tek secret verilirse (ve varsayılanda) <c>CallbackSecret</c> anahtarı yazılıyor,
/// birden fazlaysa <c>CallbackSecrets:N</c>. Compose tek anahtarlı biçimi
/// kullandığı için varsayılan yol onu da sınamış oluyor.
/// </param>
/// <param name="rateLimitBurst">
/// Verilmezse limit testin kendi request'lerini boğmayacak kadar yüksek. Verilirse
/// kova bu kadar request alıyor ve dakikada bir token yenileniyor.
/// </param>
public sealed class BankWebhookFactory(
    BankFixture bank, IReadOnlyList<string>? callbackSecrets = null, int? rateLimitBurst = null)
    : WebApplicationFactory<BankWebhookApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        var secrets = callbackSecrets ?? [TestBankSecrets.CallbackSecret];

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Bank"] = bank.ConnectionString,
                ["RateLimiting:BankCallbacks:BurstSize"] = (rateLimitBurst ?? 10_000).ToString(),
                ["RateLimiting:BankCallbacks:SustainedPerMinute"] = rateLimitBurst is null ? "10000" : "1"
            };

            if (secrets.Count == 1)
            {
                overrides[$"Banks:{TestBankSecrets.Bank}:CallbackSecret"] = secrets[0];
            }
            else
            {
                for (var i = 0; i < secrets.Count; i++)
                {
                    overrides[$"Banks:{TestBankSecrets.Bank}:CallbackSecrets:{i}"] = secrets[i];
                }
            }

            config.AddInMemoryCollection(overrides);
        });
    }
}
