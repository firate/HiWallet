using HiWallet.Fakes.Topups;
using HiWallet.Stripe.Fake;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Kart sağlayıcısının yerinde duran sahte servis. <c>topup-webhook</c>'a gerçek
/// HTTP ile bağlanıyor — imza doğrulaması dahil bütün zincir koşuyor.
/// </summary>
/// <param name="topupWebhookClient">
/// <c>topup-webhook</c> host'unun test istemcisi. Sahte sağlayıcının webhook'u
/// oraya yönleniyor; iki in-memory host arasında gerçek soket açılamadığı için.
/// </param>
/// <param name="secret">
/// <c>topup-webhook</c>'taki secret ile AYNI olmalı. Testlerden biri bunu bilerek
/// yanlış veriyor: imza doğrulamasının gerçekten çalıştığını ancak öyle görürsün.
/// </param>
public sealed class StripeFakeFactory(
    HttpClient topupWebhookClient, string? secret = null)
    : WebApplicationFactory<StripeFakeApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Gerçek adres değil; request'ler aşağıdaki handler ile test
                // sunucusuna yönleniyor. Yine de dolu olmak zorunda —
                // ValidateOnStart boş bırakılmasına izin vermiyor.
                ["Topup:WebhookUrl"] = "http://topup-webhook",
                ["Topup:WebhookSecret"] = secret ?? TopupWebhookApiFactory.StripeSecret
            }));

        builder.ConfigureServices(services =>
            services.AddHttpClient(TopupWebhookSender.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new PassthroughHandler(topupWebhookClient)));
    }
}
