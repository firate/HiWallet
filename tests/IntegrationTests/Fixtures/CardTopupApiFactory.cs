using HiWallet.CardTopup;
using HiWallet.CardTopup.Infrastructure.Jobs;
using HiWallet.CardTopup.Infrastructure.Upstream;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Kart yüklemesi servisini gerçek haliyle ayağa kaldırır. wallet-api'ye ve sağlayıcıya giden
/// istemcilerin yalnızca en alttaki handler'ı değişiyor: token aktarımı, resilience ve hata
/// aktarımı canlıdaki gibi koşuyor.
///
/// Broker bilerek ulaşılamaz (orchestrator fabrikasındaki gerekçe): kapanış outbox'ta kalıyor
/// ve test onu oradan okuyor. Tarama zamanlanmış haliyle koşmuyor; testler taramayı kendi
/// "şimdi"leriyle çağırıyor.
/// </summary>
/// <param name="walletApi">wallet-api'ye giden handler; verilmezse bağlantı kurulamıyor.</param>
/// <param name="provider">Sağlayıcıya giden handler; verilmezse bağlantı kurulamıyor.</param>
public sealed class CardTopupApiFactory(
    CardTopupFixture fixture,
    HttpMessageHandler? walletApi,
    HttpMessageHandler? provider)
    : WebApplicationFactory<CardTopupApp>
{
    private const string UnreachableHost = "rabbitmq-not-configured";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:CardTopup"] = fixture.ConnectionString,
                ["InternalServices:WalletApi:BaseUrl"] = "http://wallet-api",
                ["CardTopups:ProviderUrl"] = "http://stripe-fake"
            };

            BrokerSettings.ApplyFallbacks(overrides);
            overrides["RabbitMq:Host"] = UnreachableHost;

            config.AddInMemoryCollection(overrides);
            config.AddInMemoryCollection(TestTokens.Settings);
        });

        builder.ConfigureTestServices(services =>
        {
            TestTokens.Trust(services);
            TestStaffPermissions.Use(services);

            services.AddHttpClient(nameof(WalletHoldClient))
                .ConfigurePrimaryHttpMessageHandler(() => walletApi ?? new UnreachableHandler());

            services.AddHttpClient(CardPaymentClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => provider ?? new UnreachableHandler());

            // Zamanlanmış tarama testin verisine dokunmasın.
            services.Remove(services.Single(d => d.ImplementationType == typeof(OpenCardTopupScan)));
        });
    }
}
