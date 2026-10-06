using HiWallet.Stripe.Fake;
using HiWallet.Stripe.Fake.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Kart sağlayıcısının yerinde duran sahte servis. Webhook'u <c>topup-webhook</c>'a gerçek
/// HTTP ile gönderiyor — imza doğrulaması dahil bütün zincir koşuyor.
/// </summary>
/// <param name="topupWebhookClient">
/// <c>topup-webhook</c> host'unun test istemcisi. Sahte sağlayıcının webhook'u oraya
/// yönleniyor; iki in-memory host arasında gerçek soket açılamadığı için.
/// </param>
/// <param name="secret">
/// <c>topup-webhook</c>'taki secret ile AYNI olmalı. Testlerden biri bunu bilerek yanlış
/// veriyor: imza doğrulamasının gerçekten çalıştığını ancak öyle görürsün.
/// </param>
/// <param name="time">
/// Sağlayıcının saati; verilmezse gerçek saat. Oturumun süresinin dolduğunu görmek için ileri
/// alınabilen bir saat veriliyor.
/// </param>
public sealed class StripeFakeFactory(HttpClient topupWebhookClient, string? secret = null, TimeProvider? time = null)
    : WebApplicationFactory<StripeFakeApp>
{
    /// <summary>Ödeme sayfasının tarayıcıdan ulaşılan adresi, testte.</summary>
    public const string PublicUrl = "http://stripe-fake.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Gerçek adres değil; istekler aşağıdaki handler ile test sunucusuna
                // yönleniyor. Yine de dolu olmak zorunda — ValidateOnStart boşa izin vermiyor.
                ["StripeFake:WebhookUrl"] = "http://topup-webhook",
                ["StripeFake:WebhookSecret"] = secret ?? TopupWebhookApiFactory.StripeSecret,
                ["StripeFake:PublicUrl"] = PublicUrl
            }));

        builder.ConfigureServices(services =>
            services.AddHttpClient(PaymentWebhookSender.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new PassthroughHandler(topupWebhookClient)));

        if (time is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(time));
        }
    }
}
