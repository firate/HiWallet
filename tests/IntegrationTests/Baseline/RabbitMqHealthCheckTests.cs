using System.Diagnostics;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.IntegrationTests.Baseline;

/// <summary>
/// Broker erişilemezken sağlık ucunun NE KADAR SÜREDE cevap verdiği.
///
/// Compose koşarken çıkan kusur: <c>docker compose stop rabbitmq</c>
/// sonrası <c>/health/ready</c> 5005 ms sürüyordu, compose'un probe timeout'u ise
/// 3 sn. Probe kesiliyor, container <c>unhealthy</c> işaretleniyor ve bilerek
/// <c>Degraded</c> dönen servis trafikten çekiliyordu — broker'sız çalışabilme
/// tasarımının tam tersi.
///
/// Kayıttaki timeout bunu çözmüyordu: o yalnızca token'ı iptal ediyor,
/// <c>HealthCheckService</c> görevi terk etmiyor.
/// </summary>
public sealed class RabbitMqHealthCheckTests
{
    /// <summary>Compose'daki probe timeout'u 3 sn; kontrol bunun ALTINDA kalmalı.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private static HealthCheckService BuildService()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Yönlendirilebilir ama hiçbir şeyin dinlemediği adres: bağlantı
                // reddedilmek yerine sessizce beklesin diye 127.0.0.1 değil,
                // ayrılmış TEST-NET-1 bloğu (RFC 5737).
                ["RabbitMq:Host"] = "192.0.2.1",
                ["RabbitMq:Port"] = "5672",
                ["RabbitMq:Username"] = "yok",
                ["RabbitMq:Password"] = "yok"
            })
            .Build());

        services.AddLogging();
        services.AddHiWalletMessaging(
            services.BuildServiceProvider().GetRequiredService<IConfiguration>(),
            "health-check-test");

        services.AddHealthChecks()
            .AddRabbitMqCheck("rabbitmq", HealthStatus.Degraded);

        return services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
    }

    [Fact]
    public async Task ErisilemezBroker_ProbeTimeoutundanOnceCevapVerir()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = BuildService();

        var stopwatch = Stopwatch.StartNew();
        var report = await service.CheckHealthAsync(ct);
        stopwatch.Stop();

        report.Status.ShouldBe(HealthStatus.Degraded);
        stopwatch.Elapsed.ShouldBeLessThan(
            ProbeTimeout,
            $"Sağlık kontrolü {stopwatch.ElapsedMilliseconds} ms sürdü; compose probe'u " +
            $"{ProbeTimeout.TotalSeconds:0} sn'de kesiyor ve container unhealthy işaretleniyor.");
    }

    /// <summary>
    /// Broker yokken <c>Unhealthy</c> DEĞİL <c>Degraded</c>: uç 200 dönmeye devam
    /// etmeli. Orchestrator broker'sız da çekim kabul edebiliyor, komut outbox'ta
    /// bekliyor (decisions.md madde 32).
    /// </summary>
    [Fact]
    public async Task ErisilemezBroker_UnhealthyDegilDegradedDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = BuildService();

        var report = await service.CheckHealthAsync(ct);

        report.Entries["rabbitmq"].Status.ShouldBe(HealthStatus.Degraded);
    }
}
