using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace HiWallet.Shared.Infrastructure.OpenApi;

/// <summary>
/// OpenAPI dokümanı + Scalar arayüzü (baseline.md madde 9).
///
/// Doküman <c>Microsoft.AspNetCore.OpenApi</c> ile üretiliyor — framework'ün kendi
/// üreteci, Swashbuckle YOK. Üreteç yalnızca JSON veriyor; okunabilir arayüz için
/// Scalar ayrıca ekleniyor.
///
/// Servise özel hiçbir şey içermiyor, bu yüzden Shared'da: <c>wallet-api</c> ve
/// <c>withdrawal-orchestrator</c> aynı kurulumu istiyor ve iki yerde tutmanın anlamı
/// yok. <c>ObservabilitySetup</c> ile aynı gerekçe.
/// </summary>
public static class OpenApiSetup
{
    public static IServiceCollection AddHiWalletOpenApi(this IServiceCollection services)
    {
        return services.AddOpenApi();
    }

    /// <summary>
    /// Development kapısı BURADA, çağıran tarafta değil. Üretimde API yüzeyinin
    /// şemasını yayınlamak saldırgana harita vermek demek; kapıyı her serviste tekrar
    /// yazmak, bir gün birinde unutulması demekti. Unutulduğunda da hiçbir test
    /// kırılmaz, yalnızca uç sessizce açık kalır.
    /// </summary>
    public static WebApplication MapHiWalletOpenApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        // Doküman: /openapi/v1.json
        app.MapOpenApi();

        // Arayüz: /scalar — dokümanı yukarıdaki uçtan okuyor.
        app.MapScalarApiReference(options => options.WithTitle(app.Environment.ApplicationName));

        return app;
    }
}
