using System.Net.Sockets;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.PersonalMobileApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// personal-mobile-api'yi gerçek haliyle ayağa kaldırır; iç servislere giden
/// istemcilerin yalnızca en alttaki handler'ı değişiyor. Resilience pipeline'ı,
/// başlık aktarımı ve hata aktarımı canlıdaki gibi koşuyor.
/// </summary>
/// <param name="walletApi">
/// wallet-api'ye giden handler. Verilmezse bağlantı kurulamıyor: sağlık ve rate limit
/// testleri iç servise hiç ulaşmıyor.
/// </param>
/// <param name="rateLimitBurst">
/// Verilmezse limit testin kendi request'lerini boğmayacak kadar yüksek. Verilirse
/// iki kova da bu kadar request alıyor ve dakikada bir token yenileniyor.
/// </param>
public sealed class PersonalMobileApiFactory(
    HttpMessageHandler? walletApi = null,
    HttpMessageHandler? withdrawalOrchestrator = null,
    int? rateLimitBurst = null)
    : WebApplicationFactory<PersonalMobileApiApp>
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
                ["RateLimiting:Customer:BurstSize"] = burst,
                ["RateLimiting:Customer:SustainedPerMinute"] = sustained,
                ["RateLimiting:Withdrawals:BurstSize"] = burst,
                ["RateLimiting:Withdrawals:SustainedPerMinute"] = sustained
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(nameof(WalletApiClient))
                .ConfigurePrimaryHttpMessageHandler(() => walletApi ?? new UnreachableHandler());

            services.AddHttpClient(nameof(WithdrawalOrchestratorClient))
                .ConfigurePrimaryHttpMessageHandler(() => withdrawalOrchestrator ?? new UnreachableHandler());
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
