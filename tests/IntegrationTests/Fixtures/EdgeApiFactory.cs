using System.Net.Sockets;
using HiWallet.EdgeApi.InternalServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Bir ön API'yi gerçek haliyle ayağa kaldırır; iç servislere giden istemcilerin
/// yalnızca en alttaki handler'ı değişiyor. Resilience pipeline'ı, token ve başlık
/// aktarımı, hata aktarımı canlıdaki gibi koşuyor.
/// </summary>
/// <param name="audience">Ön API'nin kabul ettiği token hedef kitlesi: kendi adı.</param>
/// <param name="walletApi">
/// wallet-api'ye giden handler. Verilmezse bağlantı kurulamıyor: sağlık ve rate limit
/// testleri iç servise hiç ulaşmıyor.
/// </param>
/// <param name="rateLimitBurst">
/// Verilmezse limit testin kendi request'lerini boğmayacak kadar yüksek. Verilirse
/// iki kova da bu kadar request alıyor ve dakikada bir token yenileniyor.
/// </param>
/// <param name="onboarding">
/// onboarding'e giden handler; yalnızca bireysel müşterinin ön API'leri onu kullanıyor.
/// </param>
/// <param name="cardTopup">
/// kart yüklemesi servisine giden handler; yalnızca bireysel müşterinin ön API'leri onu kullanıyor.
/// </param>
public abstract class EdgeApiFactory<TEntryPoint>(
    string audience,
    HttpMessageHandler? walletApi,
    HttpMessageHandler? withdrawalOrchestrator,
    int? rateLimitBurst,
    HttpMessageHandler? onboarding = null,
    HttpMessageHandler? staffAdmin = null,
    HttpMessageHandler? cardTopup = null)
    : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var burst = (rateLimitBurst ?? 10_000).ToString();
            var sustained = rateLimitBurst is null ? "10000" : "1";

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalServices:WalletApi:BaseUrl"] = "http://wallet-api",
                ["InternalServices:WithdrawalOrchestrator:BaseUrl"] = "http://withdrawal-orchestrator",
                ["InternalServices:Onboarding:BaseUrl"] = "http://onboarding",
                ["InternalServices:StaffAdmin:BaseUrl"] = "http://staff-admin",
                ["InternalServices:CardTopup:BaseUrl"] = "http://card-topup",
                ["RateLimiting:Registration:BurstSize"] = burst,
                ["RateLimiting:Registration:SustainedPerMinute"] = sustained,
                ["RateLimiting:Client:BurstSize"] = burst,
                ["RateLimiting:Client:SustainedPerMinute"] = sustained,
                ["RateLimiting:Withdrawals:BurstSize"] = burst,
                ["RateLimiting:Withdrawals:SustainedPerMinute"] = sustained
            });

            config.AddInMemoryCollection(TestTokens.Settings);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Audience"] = audience
            });
        });

        builder.ConfigureTestServices(services =>
        {
            TestTokens.Trust(services);

            services.AddHttpClient(nameof(WalletApiClient))
                .ConfigurePrimaryHttpMessageHandler(() => walletApi ?? new UnreachableHandler());

            services.AddHttpClient(nameof(WithdrawalOrchestratorClient))
                .ConfigurePrimaryHttpMessageHandler(() => withdrawalOrchestrator ?? new UnreachableHandler());

            services.AddHttpClient(nameof(OnboardingClient))
                .ConfigurePrimaryHttpMessageHandler(() => onboarding ?? new UnreachableHandler());

            services.AddHttpClient(nameof(StaffAdminClient))
                .ConfigurePrimaryHttpMessageHandler(() => staffAdmin ?? new UnreachableHandler());

            services.AddHttpClient(nameof(CardTopupClient))
                .ConfigurePrimaryHttpMessageHandler(() => cardTopup ?? new UnreachableHandler());
        });
    }
}

/// <summary>Karşı taraf kapalıymış gibi: bağlantı kurulamıyor.</summary>
public sealed class UnreachableHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new HttpRequestException(
            "Bağlantı reddedildi.", new SocketException((int)SocketError.ConnectionRefused));
    }
}
