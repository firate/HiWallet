using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HiWallet.CardTopup.Infrastructure.Upstream;

/// <summary>
/// Kart sağlayıcısıyla konuşan tek yer. Sağlayıcının API tipleri burada, bu servisin içinde
/// yazılıyor; sahte sağlayıcıyla PAYLAŞILMIYOR (bankadaki gerekçe).
///
/// Ölçüt bankadakiyle aynı: sağlayıcının verdiği cevap kalıcı, cevap alamamak geçici.
/// </summary>
public sealed class CardPaymentClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "card-payments";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Adlandırılmış istemci: tarama singleton ve handler rotasyonunu kaçırmamalı.</summary>
    private HttpClient Client => httpClientFactory.CreateClient(HttpClientName);

    /// <summary>
    /// Ödeme açar. Referans yüklemenin kimliği: aynı referansla ikinci istek yeni ödeme
    /// açmıyor, ilkini dönüyor. Cevabı alınamayan açılış bu yüzden tekrarlanabiliyor.
    /// </summary>
    /// <exception cref="UpstreamUnavailableException">Sağlayıcı cevap vermedi.</exception>
    /// <exception cref="ProviderRejectedException">Sağlayıcı isteği reddetti.</exception>
    public async Task<ProviderPayment> OpenAsync(Domain.CardTopup topup, CancellationToken ct)
    {
        var request = new OpenPaymentRequest(topup.Id, topup.Amount, topup.Currency, ReturnUrlOf(topup), topup.ExpiresAt);

        HttpResponseMessage response;

        try
        {
            response = await Client.PostAsJsonAsync("v1/payments", request, JsonOptions, ct);
        }
        catch (Exception exception) when (Upstream.IsNoAnswer(exception, ct))
        {
            throw new UpstreamUnavailableException("Kart sağlayıcısı", exception);
        }

        using (response)
        {
            if (Upstream.IsTransient(response.StatusCode))
            {
                throw new UpstreamUnavailableException("Kart sağlayıcısı", innerException: null);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderRejectedException(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            }

            return await response.Content.ReadFromJsonAsync<ProviderPayment>(JsonOptions, ct)
                   ?? throw new UpstreamUnavailableException("Kart sağlayıcısı", innerException: null);
        }
    }

    /// <summary>
    /// Yüklemenin ödemesi, referansla. Ödeme kimliği bilinmeden de soruluyor: açılışın cevabı
    /// kaybolduysa kimliği bilmiyoruz.
    /// </summary>
    /// <returns>Bu referansla ödeme hiç açılmadıysa <c>null</c>.</returns>
    /// <exception cref="UpstreamUnavailableException">Sağlayıcı cevap vermedi.</exception>
    public async Task<ProviderPayment?> FindAsync(Guid reference, CancellationToken ct)
    {
        HttpResponseMessage response;

        try
        {
            response = await Client.GetAsync($"v1/payments?reference={reference}", ct);
        }
        catch (Exception exception) when (Upstream.IsNoAnswer(exception, ct))
        {
            throw new UpstreamUnavailableException("Kart sağlayıcısı", exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound) return null;

            if (!response.IsSuccessStatusCode)
            {
                throw new UpstreamUnavailableException("Kart sağlayıcısı", innerException: null);
            }

            return await response.Content.ReadFromJsonAsync<ProviderPayment>(JsonOptions, ct);
        }
    }

    /// <summary>
    /// Arayüzün verdiği adrese yüklemenin kimliği ekleniyor: ödeme sayfasından dönen müşterinin
    /// hangi yüklemenin sonucunu beklediğini arayüz oradan okuyor.
    /// </summary>
    private static string ReturnUrlOf(Domain.CardTopup topup) =>
        $"{topup.ReturnUrl}{(topup.ReturnUrl.Contains('?') ? '&' : '?')}cardTopupId={topup.Id}";

    private sealed record OpenPaymentRequest(
        Guid Reference, decimal Amount, string Currency, string ReturnUrl, DateTimeOffset ExpiresAt);
}

/// <summary>Sağlayıcıdaki ödeme, sağlayıcının API'sindeki haliyle.</summary>
/// <param name="Status"><see cref="ProviderPaymentStatus"/>.</param>
public sealed record ProviderPayment(
    string Id, Guid Reference, string Status, decimal Amount, string Currency, DateTimeOffset ExpiresAt, string PaymentUrl);

/// <summary>Sağlayıcının durum kelimeleri.</summary>
public static class ProviderPaymentStatus
{
    public const string RequiresPayment = "requires_payment";

    public const string Succeeded = "succeeded";

    public const string Canceled = "canceled";

    public const string Expired = "expired";
}
