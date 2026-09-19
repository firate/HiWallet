using System.Threading.Channels;

namespace HiWallet.Fakes.Topups;

/// <summary>
/// Sahte sağlayıcının kim olduğu ve nereye webhook göndereceği.
/// </summary>
public sealed class FakeProviderOptions
{
    public const string SectionName = "Topup";

    /// <summary>
    /// Sağlayıcı kimliği — <c>bank-fake</c> ya da <c>stripe-fake</c>. Konfigürasyon
    /// DEĞİL, servis kendi adını kendisi veriyor: bir sahte servis tek bir kurumu
    /// temsil eder ve çalışma anında kimliğini değiştiremez.
    /// </summary>
    public string Provider { get; internal set; } = string.Empty;

    /// <summary>Örnek: <c>http://topup-webhook:8080</c>.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>
    /// Paylaşılan HMAC secret'ı. <c>topup-webhook</c>'taki
    /// <c>Providers:{provider}:WebhookSecret</c> ile AYNI olmak zorunda; ayrışırsa
    /// her webhook <c>401</c> alır ve hiçbir para girmez.
    /// </summary>
    public string? WebhookSecret { get; init; }
}

public static class FakeProviderSetup
{
    /// <param name="provider">
    /// Bu servisin temsil ettiği kurum. Sabit veriliyor, konfigürasyondan
    /// OKUNMUYOR — yanlış konfigürasyonla bir sahtenin başka bir kurum gibi
    /// davranması, ledger'da yanlış <c>clearing</c> hesabına yazılmasına yol açardı.
    /// </param>
    public static IServiceCollection AddFakeTopupProvider(
        this IServiceCollection services, IConfiguration configuration, string provider)
    {
        services.AddOptions<FakeProviderOptions>()
            .Bind(configuration.GetSection(FakeProviderOptions.SectionName))
            .Configure(options => options.Provider = provider)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.WebhookUrl),
                $"{FakeProviderOptions.SectionName}:WebhookUrl boş; webhook gönderilemez.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.WebhookSecret),
                // İmzasız gönderen bir sağlayıcı, doğrulayıcımızın hiç sınanmaması
                // demek olurdu: her request 401 alır ve sebebi aranırdı.
                $"{FakeProviderOptions.SectionName}:WebhookSecret boş; imzasız webhook 401 alır.")
            .ValidateOnStart();

        services.AddHttpClient(TopupWebhookSender.HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<FakeProviderOptions>>().Value;

            client.BaseAddress = new Uri(options.WebhookUrl!.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<TopupWebhookSender>();

        // Gönderim kuyruğu sınırlı: sınırsız bırakılsaydı bir döngüyle tetiklenen
        // binlerce request bellekte birikirdi. Dolduğunda yazan bekliyor — sahte
        // servisin yavaşlaması, sessizce event düşürmesinden iyi.
        services.AddSingleton(Channel.CreateBounded<TopupDelivery>(
            new BoundedChannelOptions(capacity: 256) { FullMode = BoundedChannelFullMode.Wait }));

        services.AddHostedService<TopupDeliveryWorker>();

        return services;
    }
}

/// <summary>
/// Kuyruğa düşen gönderimleri sırayla işler.
///
/// <b>Neden arka planda.</b> HTTP endpoint'i <c>202</c> dönüp çekiliyor; gecikmeli ve
/// çok event'li modlar request'i saniyelerce açık tutardı. Gerçek bir sağlayıcı da
/// webhook'u senin request'in bitince gönderiyor.
///
/// <b>Kalıcı DEĞİL, bilerek.</b> Süreç ölürse kuyruktakiler kaybolur ve tetikleyen
/// taraf — bir test ya da insan — yeniden tetikler.
///
/// <b>Gönderimler TEK TEK işleniyor.</b> Gecikmeli bir gönderim beklerken arkasındaki
/// tetiklemeler de bekliyor (en fazla 30 sn). Bilinen bir sınır; paralel işlemek
/// aynı cüzdana yapılan tetiklemelerin sırasını da bozardı.
/// </summary>
internal sealed class TopupDeliveryWorker(
    Channel<TopupDelivery> queue,
    TopupWebhookSender sender,
    ILogger<TopupDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var delivery in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(delivery, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Bir gönderimin hatası worker'ı öldürmüyor. Öldürseydi sahte
                // sağlayıcı sessizce susar ve "neden webhook gelmiyor" diye
                // aranırdı.
                logger.LogError(
                    exception, "Top-up gönderimi başarısız. Cüzdan {WalletId}", delivery.WalletId);
            }
        }
    }
}
