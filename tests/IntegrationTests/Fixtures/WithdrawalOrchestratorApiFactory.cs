using HiWallet.WithdrawalOrchestrator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// withdrawal-orchestrator'ı gerçek haliyle ayağa kaldırır: model doğrulama,
/// IBAN kontrolü, saga ve outbox yazımı baştan sona koşuyor.
/// </summary>
/// <param name="useRealBroker">
/// <c>false</c> (varsayılan) ise broker adresi bilerek ulaşılamaz bırakılıyor. Bu
/// bir eksiklik değil, testin konusu: outbox'ın varlık sebebi broker yokken de
/// çekim kabul edebilmek. Relay ile tüketici arka planda yeniden deneyip duruyor,
/// HTTP yolu etkilenmiyor — ve koşu broker'da kuyruk bırakmıyor.
/// </param>
public sealed class WithdrawalOrchestratorApiFactory(
    OrchestratorFixture fixture, bool useRealBroker = false)
    : WebApplicationFactory<WithdrawalOrchestratorApp>
{
    private const string UnreachableHost = "rabbitmq-not-configured";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Withdrawal"] = fixture.ConnectionString
            };

            // Ayarlar her hâlükârda gerekli: AddHiWalletMessaging ValidateOnStart ile
            // doğruluyor ve eksik bırakılsa host hiç kurulamazdı.
            BrokerSettings.ApplyFallbacks(overrides);

            if (!useRealBroker)
            {
                overrides["RabbitMq:Host"] = UnreachableHost;
            }

            config.AddInMemoryCollection(overrides);
        });
    }
}
