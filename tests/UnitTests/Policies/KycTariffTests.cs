using System.Text.Json;
using System.Text.Json.Nodes;

namespace HiWallet.UnitTests.Policies;

/// <summary>
/// Seviye tarifesi iki uygulamada yazılı: wallet-api transferi, ödemeyi ve kartla yüklemenin
/// payını, wallet-consumer çekimi ve havaleyi kontrol ediyor. Müşteriye gösterilen limitler
/// wallet-api'den okunuyor. İki tarife ayrışırsa müşteri gördüğünden farklı bir limitle
/// reddedilirdi; bu test ayrışmayı derlemede değil testte yakalıyor.
/// </summary>
public sealed class KycTariffTests
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [Fact]
    public void Tarife_IkiUygulamadaAyni()
    {
        var api = KycSection("src/core/wallet/WalletApi/appsettings.json");
        var consumer = KycSection("src/core/wallet/WalletConsumer/appsettings.json");

        JsonNode.DeepEquals(api, consumer).ShouldBeTrue(
            $"wallet-api: {api.ToJsonString()}\nwallet-consumer: {consumer.ToJsonString()}");
    }

    private static JsonNode KycSection(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        var settings = JsonNode.Parse(File.ReadAllText(path), documentOptions: Options)!;

        return settings["Kyc"] ?? throw new InvalidOperationException($"{relativePath} içinde Kyc bölümü yok.");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HiWallet.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repo kökü bulunamadı (HiWallet.sln).");
    }
}
