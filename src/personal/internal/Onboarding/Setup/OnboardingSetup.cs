using HiWallet.Onboarding.Application;
using HiWallet.Onboarding.Application.Abstractions;
using HiWallet.Onboarding.Infrastructure;
using HiWallet.Onboarding.Infrastructure.Keycloak;
using HiWallet.Onboarding.Infrastructure.Messaging;
using HiWallet.Onboarding.Infrastructure.Persistence;
using HiWallet.Onboarding.Infrastructure.PopulationRegistry;
using HiWallet.Onboarding.Infrastructure.Wallet;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HiWallet.Onboarding.Setup;

public static class OnboardingSetup
{
    /// <summary>Bu servisin kendi veritabanı, kendi Postgres sunucusunda.</summary>
    public const string ConnectionStringName = "Onboarding";

    private const string KeycloakTokenClient = "keycloak-token";

    public static IServiceCollection AddOnboarding(this IServiceCollection services)
    {
        // Bağlantı dizesi ve adresler KAYIT anında değil, kullanılırken okunuyor:
        // WebApplicationFactory konfigürasyonunu host kurulduktan sonra ekliyor.
        services.AddDbContextFactory<OnboardingDbContext>((provider, options) =>
            options.UseNpgsql(
                provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName),
                npgsql => npgsql.CommandTimeout(DbTimeouts.CommandSeconds)));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<RegistrationService>();
        services.AddScoped<VerificationService>();
        services.AddScoped<PhoneChangeService>();
        services.AddScoped<HolderCheckService>();
        services.AddScoped<CustomerLookupService>();

        services.AddOptions<PhoneChangeOptions>()
            .BindConfiguration(PhoneChangeOptions.SectionName)
            .Validate(o => o.ReauthenticationWindow > TimeSpan.Zero && o.WithdrawalHold > TimeSpan.Zero,
                $"{PhoneChangeOptions.SectionName}: ReauthenticationWindow ve WithdrawalHold sıfırdan büyük olmalı.")
            .ValidateOnStart();

        // Fail fast: eksik ayar ilk kayıtta değil başlangıçta patlasın.
        services.AddOptions<DocumentOptions>()
            .BindConfiguration(DocumentOptions.SectionName)
            .Validate(d => !string.IsNullOrWhiteSpace(d.Terms) && !string.IsNullOrWhiteSpace(d.PrivacyNotice),
                $"{DocumentOptions.SectionName}: Terms ve PrivacyNotice sürümleri zorunlu.")
            .ValidateOnStart();

        services.AddOptions<KeycloakOptions>()
            .BindConfiguration(KeycloakOptions.SectionName)
            .Validate(k => Uri.TryCreate(k.BaseUrl, UriKind.Absolute, out _)
                           && !string.IsNullOrWhiteSpace(k.Realm)
                           && !string.IsNullOrWhiteSpace(k.ClientId)
                           && !string.IsNullOrWhiteSpace(k.ClientSecret),
                $"{KeycloakOptions.SectionName}: BaseUrl, Realm, ClientId ve ClientSecret zorunlu.")
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .Validate(e => !string.IsNullOrWhiteSpace(e.Host) && !string.IsNullOrWhiteSpace(e.From),
                $"{EmailOptions.SectionName}: Host ve From zorunlu.")
            .ValidateOnStart();

        // Servisin kendi token'ı tek bir örnekte önbellekleniyor.
        services.AddHttpClient(KeycloakTokenClient, (provider, http) =>
            http.BaseAddress = WithSlash(provider.GetRequiredService<IOptions<KeycloakOptions>>().Value.BaseUrl));
        services.AddSingleton<IServiceTokens>(provider => new KeycloakServiceTokens(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(KeycloakTokenClient),
            provider.GetRequiredService<IOptions<KeycloakOptions>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddTransient<ServiceTokenHandler>();

        services.AddHttpClient<IIdentityProvider, KeycloakIdentityProvider>((provider, http) =>
            {
                var keycloak = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
                http.BaseAddress = WithSlash($"{keycloak.BaseUrl.TrimEnd('/')}/admin/realms/{keycloak.Realm}");
            })
            .AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient<WalletAccountsClient>(nameof(WalletAccountsClient), (provider, http) =>
                http.BaseAddress = WithSlash(Required(provider, "InternalServices:WalletApi:BaseUrl")))
            .AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient<ISmsSender, SmsApiClient>((provider, http) =>
            http.BaseAddress = WithSlash(Required(provider, "Sms:BaseUrl")));

        services.AddHttpClient<IPopulationRegistry, PopulationRegistryClient>((provider, http) =>
            http.BaseAddress = WithSlash(Required(provider, "PopulationRegistry:BaseUrl")));

        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddHealthChecks()
            // Postgres olmadan kayıt başlatılamıyor: servis trafikten çekilmeli.
            .AddNpgSql(
                connectionStringFactory: provider => provider
                    .GetRequiredService<IConfiguration>()
                    .GetConnectionString(ConnectionStringName)
                    ?? throw new InvalidOperationException("Bağlantı dizesi yok."),
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthCheckEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(3));

        return services;
    }

    private static string Required(IServiceProvider provider, string key) =>
        provider.GetRequiredService<IConfiguration>()[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Zorunlu konfigürasyon eksik: {key}.");

    /// <summary>Göreli adresler taban adresin son parçasını silmesin diye sonda eğik çizgi.</summary>
    private static Uri WithSlash(string url) => new(url.EndsWith('/') ? url : url + "/");
}
