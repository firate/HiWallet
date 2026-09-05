using HiWallet.WithdrawalOrchestrator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// withdrawal-orchestrator'ı gerçek haliyle ayağa kaldırır: model doğrulama,
/// IBAN kontrolü, saga ve outbox yazımı baştan sona koşuyor.
/// </summary>
public sealed class WithdrawalOrchestratorApiFactory(OrchestratorFixture fixture)
    : WebApplicationFactory<WithdrawalOrchestratorApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Withdrawal"] = fixture.ConnectionString
            }));
    }
}
