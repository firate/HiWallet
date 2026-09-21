using HiWallet.TopupWebhook;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// topup-webhook'u gerçek haliyle ayağa kaldırır: imza doğrulama, rate limiter,
/// model doğrulama ve inbox yazımı zinciri baştan sona koşuyor.
/// </summary>
/// <param name="stripeSecrets">
/// Tek secret verilirse (ve varsayılanda) <c>WebhookSecret</c> anahtarı yazılıyor,
/// birden fazlaysa <c>WebhookSecrets:N</c>. Compose tek anahtarlı biçimi kullandığı
/// için varsayılan yol onu da sınamış oluyor.
/// </param>
public sealed class TopupWebhookApiFactory(
    InboxFixture inbox, IReadOnlyList<string>? stripeSecrets = null)
    : WebApplicationFactory<TopupWebhookApp>
{
    /// <summary>
    /// Testlerin imzaladığı secret. Gerçek bir sır değil — canlıda ortamdan gelir,
    /// burada testin kendi kontrolünde olması gerekiyor ki imzayı üretebilsin.
    /// </summary>
    public const string StripeSecret = "test-secret-stripe";

    public const string StripeProvider = "stripe-fake";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        var secrets = stripeSecrets ?? [StripeSecret];

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Topup"] = inbox.ConnectionString,

                // Rate limiter testin kendi request'lerini boğmasın: burada ölçülen şey
                // limitleme değil, imza ve inbox davranışı.
                ["RateLimiting:Webhooks:BurstSize"] = "10000",
                ["RateLimiting:Webhooks:SustainedPerMinute"] = "10000"
            };

            if (secrets.Count == 1)
            {
                overrides[$"Providers:{StripeProvider}:WebhookSecret"] = secrets[0];
            }
            else
            {
                for (var i = 0; i < secrets.Count; i++)
                {
                    overrides[$"Providers:{StripeProvider}:WebhookSecrets:{i}"] = secrets[i];
                }
            }

            BrokerSettings.ApplyFallbacks(overrides);

            config.AddInMemoryCollection(overrides);
        });
    }
}
