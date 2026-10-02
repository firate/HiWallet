using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Persistence;
using HiWallet.StaffAdmin.Application;
using HiWallet.StaffAdmin.Application.Abstractions;
using HiWallet.StaffAdmin.Domain;
using HiWallet.StaffAdmin.Infrastructure.Keycloak;
using HiWallet.StaffAdmin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HiWallet.StaffAdmin.Setup;

public static class StaffAdminSetup
{
    /// <summary>Bu servisin kendi veritabanı, kendi Postgres sunucusunda.</summary>
    public const string ConnectionStringName = "StaffAdmin";

    private const string KeycloakTokenClient = "keycloak-token";

    public static IServiceCollection AddStaffAdmin(this IServiceCollection services)
    {
        // Bağlantı dizesi ve adresler KAYIT anında değil, kullanılırken okunuyor:
        // WebApplicationFactory konfigürasyonunu host kurulduktan sonra ekliyor.
        services.AddDbContextFactory<StaffAdminDbContext>((provider, options) =>
            options.UseNpgsql(
                provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName),
                npgsql => npgsql.CommandTimeout(DbTimeouts.CommandSeconds)));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<StaffAudit>();
        services.AddScoped<RoleService>();
        services.AddScoped<StaffService>();
        services.AddHostedService<StaffAdminBootstrap>();

        // Fail fast: eksik ayar ilk istekte değil başlangıçta patlasın.
        services.AddOptions<KeycloakOptions>()
            .BindConfiguration(KeycloakOptions.SectionName)
            .Validate(k => Uri.TryCreate(k.BaseUrl, UriKind.Absolute, out _)
                           && !string.IsNullOrWhiteSpace(k.Realm)
                           && !string.IsNullOrWhiteSpace(k.ClientId)
                           && !string.IsNullOrWhiteSpace(k.ClientSecret),
                $"{KeycloakOptions.SectionName}: BaseUrl, Realm, ClientId ve ClientSecret zorunlu.")
            .Validate(k => k.Invitation.LifespanHours > 0,
                $"{KeycloakOptions.SectionName}:Invitation:LifespanHours pozitif olmalı.")
            .ValidateOnStart();

        services.AddOptions<StaffAdminOptions>()
            .BindConfiguration(StaffAdminOptions.SectionName)
            .Validate(o => StaffRoleRules.IsWellFormed(o.Bootstrap.AdminRoleName)
                           && !StaffRoleRules.IsReserved(o.Bootstrap.AdminRoleName),
                $"{StaffAdminOptions.SectionName}:Bootstrap:AdminRoleName geçerli bir rol adı olmalı.")
            .ValidateOnStart();

        // Servisin kendi token'ı tek bir örnekte önbellekleniyor.
        services.AddHttpClient(KeycloakTokenClient, (provider, http) =>
            http.BaseAddress = WithSlash(provider.GetRequiredService<IOptions<KeycloakOptions>>().Value.BaseUrl));
        services.AddSingleton(provider => new KeycloakServiceTokens(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(KeycloakTokenClient),
            provider.GetRequiredService<IOptions<KeycloakOptions>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddTransient<ServiceTokenHandler>();

        services.AddHttpClient<IStaffDirectory, KeycloakStaffDirectory>((provider, http) =>
            {
                var keycloak = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
                http.BaseAddress = WithSlash($"{keycloak.BaseUrl.TrimEnd('/')}/admin/realms/{keycloak.Realm}");
            })
            .AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHealthChecks()
            // Postgres olmadan değişikliğin kaydı yazılamıyor: servis trafikten çekilmeli.
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

    /// <summary>Göreli adresler taban adresin son parçasını silmesin diye sonda eğik çizgi.</summary>
    private static Uri WithSlash(string url) => new(url.EndsWith('/') ? url : url + "/");
}
