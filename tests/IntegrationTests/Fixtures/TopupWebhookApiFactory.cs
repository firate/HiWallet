using HiWallet.TopupWebhook;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// topup-webhook'u gerçek haliyle ayağa kaldırır: imza doğrulama, rate limiter,
/// model doğrulama ve inbox yazımı zinciri baştan sona koşuyor.
/// </summary>
public sealed class TopupWebhookApiFactory(InboxFixture inbox) : WebApplicationFactory<TopupWebhookApp>
{
    /// <summary>
    /// Testlerin imzaladığı secret. Gerçek bir sır değil — üretimde ortamdan gelir,
    /// burada testin kendi kontrolünde olması gerekiyor ki imzayı üretebilsin.
    /// </summary>
    public const string StripeSecret = "test-secret-stripe";

    public const string StripeProvider = "stripe-fake";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Topup"] = inbox.ConnectionString,
                [$"Providers:{StripeProvider}:WebhookSecret"] = StripeSecret,

                // Rate limiter testin kendi isteklerini boğmasın: burada ölçülen şey
                // limitleme değil, imza ve inbox davranışı.
                ["RateLimiting:Webhooks:BurstSize"] = "10000",
                ["RateLimiting:Webhooks:SustainedPerMinute"] = "10000"
            };

            BrokerSettings.ApplyFallbacks(overrides);

            config.AddInMemoryCollection(overrides);
        });
    }
}
