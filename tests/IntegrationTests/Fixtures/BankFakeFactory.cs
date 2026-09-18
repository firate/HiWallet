using HiWallet.Bank.Fake;
using HiWallet.Bank.Fake.Application;
using Microsoft.AspNetCore.Hosting;
using HiWallet.Bank.Fake.Infrastructure.Callbacks;
using HiWallet.Bank.Fake.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Sahte banka host'u. Adaptör buna HTTP ile bağlanıyor — testte de gerçek bir
/// HTTP sınırı var, doğrudan handler çağrısı yok. Sınırın gerçek olması önemli:
/// gerçek entegrasyonda kırılacak yer tam olarak orası.
///
/// Veritabanı YOK: sahte bankanın hafızası bellekte ve her fabrika boş bir
/// bankayla başlıyor. Testler bu yüzden birbirinin transferlerini görmüyor.
/// </summary>
/// <param name="defaultOutcome">
/// Senaryosu kurulmamış çekimlerin sonucu. Zincir testinde yarışı kaldırıyor:
/// saga kimliği ancak çekim request'i kabul edildikten sonra öğrenilebiliyor, yani
/// senaryoyu API'den kurmak teorik olarak banka çağrısına geç kalabilir.
/// Varsayılanı servis başlamadan vermek bu pencereyi tamamen kapatıyor.
/// </param>
/// <param name="callbackUrl">
/// Verilirse banka sonucu buraya POST ediyor. Verilmezse callback KAPALI ve sonuç
/// yalnızca durum sorgusuyla öğrenilebiliyor — mutabakat taramasını tek yol yapan
/// kurulum bu (decisions.md madde 35).
/// </param>
/// <param name="callbackHttpClient">
/// <c>bank-webhook</c> host'unun test istemcisi. Verildiğinde bankanın callback
/// çağrısı oraya yönleniyor ve ASIL YOL uçtan uca koşuyor — imza doğrulaması ve
/// inbox dahil.
/// </param>
public sealed class BankFakeFactory(
    TransferOutcome? defaultOutcome = null,
    string? callbackUrl = null,
    string? callbackSecret = null,
    HttpClient? callbackHttpClient = null,
    TimeSpan? settlementDelay = null)
    : WebApplicationFactory<BankFakeApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                // Testte gecikme kısa: asenkron pencere GÖRÜNÜR olmalı ama koşuyu
                // uzatmamalı. Sıfır verilemez — sıfır olsaydı sonuç kabul anında
                // hazır olur ve "pending" durumu testlerde hiç gözlemlenemezdi.
                ["BankFake:SettlementDelay"] =
                    (settlementDelay ?? TimeSpan.FromMilliseconds(200)).ToString(),

                // Para girişi tarafı bu testlerde kullanılmıyor, ama ayarı açılışta
                // doğrulanıyor: verilmezse host hiç başlamaz. Giriş tarafını
                // FakeProviderTests sınıyor.
                ["Topup:WebhookUrl"] = "http://topup-webhook",
                ["Topup:WebhookSecret"] = "kullanilmiyor"
            };

            if (defaultOutcome is { } outcome)
            {
                overrides["BankFake:DefaultOutcome"] = outcome.ToString();
                overrides["BankFake:DefaultTransientFailures"] = "2";
            }

            if (callbackUrl is not null)
            {
                overrides["BankFake:Callback:Enabled"] = "true";
                overrides["BankFake:Callback:Url"] = callbackUrl;
                overrides["BankFake:Callback:Secret"] = callbackSecret ?? TestBankSecrets.CallbackSecret;
                overrides["BankFake:Callback:PollInterval"] = "00:00:00.100";
            }

            config.AddInMemoryCollection(overrides);
        });

        if (callbackHttpClient is null) return;

        builder.ConfigureServices(services =>
            services.AddHttpClient(CallbackDispatcher.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new PassthroughHandler(callbackHttpClient)));
    }

    /// <summary>
    /// Bankanın, belirtilen client reference için açtığı transfer sayısı.
    /// Verilmezse bütün transferler. Sahte bankanın içine bakmanın tek yolu bu —
    /// HTTP'de "kaç transfer açtın" endpoint'i yok, gerçek bankada da olmazdı.
    /// </summary>
    public int TransferCount(string? clientReference = null)
    {
        var store = Services.GetRequiredService<BankFakeStore>();

        lock (store.Gate)
        {
            return store.TransfersByKey.Values.Count(t =>
                clientReference is null || t.ClientReference == clientReference);
        }
    }
}

/// <summary>
/// Testlerde kullanılan paylaşılan secret. Gerçekte <c>.env</c>'den gelir ve koda
/// GÖMÜLMEZ; burada gömülü olması sorun değil çünkü koruduğu bir şey yok — sahte
/// bankanın sahte imzası.
/// </summary>
public static class TestBankSecrets
{
    public const string Bank = "bank-fake";

    public const string CallbackSecret = "test-callback-secret";
}
