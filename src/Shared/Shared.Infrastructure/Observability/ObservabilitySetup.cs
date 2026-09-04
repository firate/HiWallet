using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HiWallet.Shared.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry: traces, metrics, logs (baseline.md madde 2 ve 3). Serilog YOK.
///
/// Üç sinyal de aynı OTLP endpoint'ine gidiyor; homelab'daki Collector onları
/// Tempo / Prometheus / Loki'ye dağıtıyor. Log–trace korelasyonu bedava geliyor:
/// OTel logs trace_id'yi log record'una kendisi gömüyor, elle korelasyon alanı
/// taşımaya gerek yok.
///
/// Servise özel hiçbir şey içermiyor, bu yüzden Shared'da: top-up hattıyla birlikte
/// ikinci bir servis geldi ve aynı kurulumu iki yerde tutmanın anlamı yok.
/// </summary>
public static class ObservabilitySetup
{
    public static IHostApplicationBuilder AddHiWalletObservability(
        this IHostApplicationBuilder builder, string defaultServiceName)
    {
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? defaultServiceName;

        // Kendi span'lerimizin kaynağı. Assembly adı zaten HiWallet.<Servis>;
        // ayrı bir sabit tutmak ikinci bir doğruluk kaynağı olurdu.
        var activitySourceName = builder.Environment.ApplicationName;
        builder.Services.AddSingleton(new ActivitySource(activitySourceName));

        // Endpoint yoksa exporter EKLENMİYOR. Aksi halde SDK localhost:4317'ye bağlanmaya
        // çalışıp her export denemesinde hata basıyor — collector'sız local geliştirmede
        // log'ları kullanılamaz hale getiriyor.
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var exportEnabled = !string.IsNullOrWhiteSpace(otlpEndpoint);

        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: typeof(ObservabilitySetup).Assembly
                .GetName().Version?.ToString() ?? "0.0.0")
            .AddEnvironmentVariableDetector();

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resource);

            // Mesaj şablonunu ve scope'ları da gönder; yoksa Loki'de yalnızca
            // formatlanmış metin kalıyor ve alan bazlı sorgu yapılamıyor.
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;

            if (exportEnabled)
            {
                logging.AddOtlpExporter();
            }
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddSource(activitySourceName)
                    // RabbitMQ.Client 7 publish/consume span'lerini kendisi üretiyor.
                    // Bunlar olmadan trace webhook'ta kesilir, tüketicide yeniden
                    // başlardı — uçtan uca takip tam da bu iki span'e bağlı.
                    .AddSource("RabbitMQ.Client.Publisher")
                    .AddSource("RabbitMQ.Client.Subscriber")
                    .AddAspNetCoreInstrumentation(options =>
                        // Health check'ler saniyede bir geliyor ve hiçbir şey anlatmıyor;
                        // trace'i doldurup Tempo'da gürültü yaratıyorlar.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();

                if (exportEnabled)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    // RED: request count, latency, error rate — üçü de bu iki
                    // instrumentation'dan geliyor.
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (exportEnabled)
                {
                    metrics.AddOtlpExporter();
                }
            });

        return builder;
    }
}
