using HiWallet.BankAdapter;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Bankaya bakan adaptör host'u. Bankaya gerçekten HTTP ile gidiyor; hedefi
/// <paramref name="bankBaseUrl"/> belirliyor.
/// </summary>
/// <param name="bankHttpClient">
/// Sahte bankanın <c>WebApplicationFactory</c>'sinden alınan istemci. Verildiğinde
/// adaptörün <c>HttpClient</c>'ı bununla değiştiriliyor: iki in-memory host
/// arasında gerçek bir soket açılamaz, ama HTTP katmanının tamamı (serileştirme,
/// başlıklar, durum kodları) yine de koşuyor.
/// </param>
/// <param name="reconciliationInterval">
/// Testte kısa. Canlıda saatler mertebesinde — callback asıl yol, tarama kontrol
/// (decisions.md madde 35).
/// </param>
/// <param name="staleAfter">
/// Testte sıfıra yakın: tarama ilk turda bekleyen transferi görsün. Canlıda
/// dakikalar mertebesinde, çünkü callback'in gelmesine zaman tanınması gerekiyor.
/// </param>
public sealed class BankAdapterFactory(
    BankFixture bank,
    string bankBaseUrl,
    HttpClient? bankHttpClient = null,
    TimeSpan? reconciliationInterval = null,
    TimeSpan? staleAfter = null)
    : WebApplicationFactory<BankAdapterApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Bank"] = bank.ConnectionString,
                ["Bank:BaseUrl"] = bankBaseUrl,
                ["Bank:RequestTimeout"] = "00:00:05",
                ["Bank:Reconciliation:Interval"] =
                    (reconciliationInterval ?? TimeSpan.FromMinutes(30)).ToString(),
                ["Bank:Reconciliation:StaleAfter"] =
                    (staleAfter ?? TimeSpan.FromMinutes(15)).ToString()
            };

            BrokerSettings.ApplyFallbacks(overrides);

            config.AddInMemoryCollection(overrides);
        });

        if (bankHttpClient is null) return;

        builder.ConfigureServices(services =>
        {
            // İki in-memory host arasında gerçek soket yok. Adaptörün adlandırılmış
            // istemcisinin handler'ı sahte bankanın test sunucusununkiyle
            // değiştiriliyor; istek/yanıt boru hattının geri kalanı olduğu gibi
            // koşuyor.
            services.AddHttpClient(HiWallet.BankAdapter.Application.BankClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new PassthroughHandler(bankHttpClient));
        });
    }
}
