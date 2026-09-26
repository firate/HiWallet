using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Extensions;

namespace HiWallet.EdgeApi.InternalServices;

/// <summary>
/// İç servise JSON istek atar, başarılı cevabı çağıranın tipine okur. Başarısız cevap
/// <see cref="InternalServiceException"/> olarak fırlatılıyor ve istemciye aynen
/// dönüyor: 400, 404, 409 ve 422'nin kararı iç serviste, ön API onu yeniden
/// yorumlamıyor.
///
/// Cevap ön API'nin kendi tipine okunuyor. İç sözleşme ile ön API'nin sözleşmesi
/// bugün aynı şekilde; ayrıştıkları gün eşleme ön API'nin controller'ına ekleniyor.
/// </summary>
public abstract class InternalServiceClient(HttpClient http)
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);

        return await ReadAsync<T>(response, ct);
    }

    /// <param name="idempotencyKey">
    /// İstemciden geldiği gibi taşınıyor. Yoksa başlık eklenmiyor ve reddi iç servis
    /// veriyor: anahtarın zorunlu olduğu kural tek yerde duruyor.
    /// </param>
    public async Task<T> PostAsync<T>(string path, object body, string? idempotencyKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
        }

        using var response = await http.SendAsync(request, ct);

        return await ReadAsync<T>(response, ct);
    }

    /// <summary>
    /// Sayfalı sorgunun adresi. Verilmeyen parametre iletilmiyor: varsayılan sayfa boyutu
    /// ve tavan iç servisin bilgisi, ön API'de ikinci kopyası tutulmuyor.
    /// </summary>
    public static string Paged(string path, string? after, int? size)
    {
        var query = new QueryBuilder();

        if (after is not null)
        {
            query.Add("after", after);
        }

        if (size is not null)
        {
            query.Add("size", size.Value.ToString(CultureInfo.InvariantCulture));
        }

        return path + query;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InternalServiceException(
                response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                await response.Content.ReadAsByteArrayAsync(ct));
        }

        return await response.Content.ReadFromJsonAsync<T>(ct)
               ?? throw new InvalidOperationException("İç servis boş gövde döndü.");
    }
}

/// <summary>İç ağdaki <c>wallet-api</c>. Ledger'a giden her istek buradan geçiyor.</summary>
public sealed class WalletApiClient(HttpClient http) : InternalServiceClient(http);

/// <summary>İç ağdaki <c>withdrawal-orchestrator</c>: çekim başlatma ve durumu.</summary>
public sealed class WithdrawalOrchestratorClient(HttpClient http) : InternalServiceClient(http);
