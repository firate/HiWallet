using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Testlerde broker ayarı. Ortamda gerçek bir RabbitMQ varsa (<c>RabbitMq__Host</c>)
/// o kullanılıyor; yoksa yalnızca fail-fast doğrulamayı geçecek yer tutucu değerler
/// veriliyor.
///
/// Yer tutucu neden gerekli: <c>AddHiWalletMessaging</c> ayarları
/// <c>ValidateOnStart</c> ile doğruluyor. Eksik bırakılsaydı host hiç kurulamaz ve
/// broker'la hiç ilgisi olmayan testler de düşerdi. Bağlanamamak sorun değil —
/// tüketici arka planda yeniden deniyor, HTTP yolu etkilenmiyor.
/// </summary>
internal static class BrokerSettings
{
    private const string PlaceholderHost = "rabbitmq-not-configured";

    /// <summary>
    /// Koşuya özel exchange/kuyruk ön eki. Aynı broker'a bakan iki koşu ön eksiz
    /// çalışsaydı biri diğerinin kuyruğundan mesaj çeker ve testler rastgele
    /// düşerdi. Tek bir statik değer: webhook ile tüketicinin AYNI topolojiye
    /// bakması şart.
    /// </summary>
    public static readonly string NamePrefix = $"it-{Guid.NewGuid().ToString("N")[..8]}.";

    public static string? Host => Environment.GetEnvironmentVariable("RabbitMq__Host");

    /// <summary>Ortamda gerçek bir broker adresi tanımlı mı.</summary>
    public static bool Configured => !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// Ortamda tanımlı OLMAYAN alanlar için varsayılan. Env sağlayıcısı
    /// <c>RabbitMq__*</c> değişkenlerini zaten okuduğu için, tanımlı alanların
    /// üzerine yazılmıyor.
    /// </summary>
    public static void ApplyFallbacks(IDictionary<string, string?> settings)
    {
        Set(settings, "RabbitMq:Host", "RabbitMq__Host", PlaceholderHost);
        Set(settings, "RabbitMq:Port", "RabbitMq__Port", "5672");
        Set(settings, "RabbitMq:Username", "RabbitMq__Username", "guest");
        Set(settings, "RabbitMq:Password", "RabbitMq__Password", "guest");

        settings["RabbitMq:NamePrefix"] = NamePrefix;
    }

    /// <param name="namePrefix">Koşunun ön eki yerine; koşuyla karışmaması gereken bir topoloji için.</param>
    public static RabbitMqOptions BuildOptions(string clientName, string? namePrefix = null) => new()
    {
        Host = Host ?? PlaceholderHost,
        Port = int.TryParse(Environment.GetEnvironmentVariable("RabbitMq__Port"), out var port) ? port : 5672,
        Username = Environment.GetEnvironmentVariable("RabbitMq__Username") ?? "guest",
        Password = Environment.GetEnvironmentVariable("RabbitMq__Password") ?? "guest",
        NamePrefix = namePrefix ?? NamePrefix,
        ClientName = clientName
    };

    /// <summary>
    /// Broker uçtan uca testleri koşturabilecek durumda mı. Uçtan uca testler bunu sorup
    /// değilse kendini atlıyor — kurulumu zorunlu kılmak yerine, varsa doğruluyor
    /// (<c>AppRolePrivilegeTests</c> ile aynı yaklaşım). Bağlanabilmek yetiyor: bütün
    /// topolojiler düz direct ve fanout exchange, eklenti istemiyor.
    /// </summary>
    public static async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        if (!Configured) return false;

        try
        {
            await using var connection = new RabbitMqConnection(
                Options.Create(BuildOptions("hiwallet-tests-probe")));

            await using var channel = await (await connection.GetAsync(ct))
                .CreateChannelAsync(cancellationToken: ct);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Set(
        IDictionary<string, string?> settings, string key, string environmentVariable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);

        settings[key] = string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
