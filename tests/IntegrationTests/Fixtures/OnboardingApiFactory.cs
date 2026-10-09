using HiWallet.Onboarding;
using HiWallet.Onboarding.Application.Abstractions;
using HiWallet.Onboarding.Infrastructure.Wallet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// onboarding'i gerçek haliyle ayağa kaldırır. Wallet gerçek (wallet-api, HTTP üzerinden);
/// Keycloak, e-posta, SMS ve nüfus kaydı başka kurumlar ya da ayrı sunucular, onların
/// yerine sahteler duruyor.
/// </summary>
public sealed class OnboardingApiFactory(OnboardingFixture fixture, HttpMessageHandler walletApi)
    : WebApplicationFactory<OnboardingApp>
{
    public FakeIdentityProvider IdentityProvider { get; } = new();

    public CapturingEmailSender Emails { get; } = new();

    public CapturingSmsSender Sms { get; } = new();

    public ScriptedPopulationRegistry Registry { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Onboarding"] = fixture.ConnectionString,
                ["InternalServices:WalletApi:BaseUrl"] = "http://wallet-api",
                // Keycloak'a gidilmiyor (sahteler aşağıda) ama ayar başlangıçta doğrulanıyor.
                ["Keycloak:ClientSecret"] = "test-gizli-anahtar"
            });

            config.AddInMemoryCollection(TestTokens.Settings);
        });

        builder.ConfigureTestServices(services =>
        {
            TestTokens.Trust(services);
            TestStaffPermissions.Use(services);

            services.AddSingleton<IIdentityProvider>(IdentityProvider);
            services.AddSingleton<IEmailSender>(Emails);
            services.AddSingleton<ISmsSender>(Sms);
            services.AddSingleton<IPopulationRegistry>(Registry);
            services.AddSingleton<IServiceTokens, TestServiceTokens>();

            services.AddHttpClient(nameof(WalletAccountsClient))
                .ConfigurePrimaryHttpMessageHandler(() => walletApi);
        });
    }
}
