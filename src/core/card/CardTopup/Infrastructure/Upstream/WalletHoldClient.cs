using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace HiWallet.CardTopup.Infrastructure.Upstream;

/// <summary>
/// wallet-api'nin limit payı ucu. Müşterinin token'ı AYNEN iletiliyor: payı wallet müşteri
/// adına ayırıyor, cüzdanın sahibi olduğunu kendisi doğruluyor.
///
/// Yalnızca istek kapsamında çağrılıyor; taramanın elinde müşterinin token'ı yok ve pay
/// istemiyor, yalnızca kapatıyor.
/// </summary>
public sealed class WalletHoldClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Payı ayırır. Aynı yükleme için ikinci istek yeni pay açmıyor, wallet mevcut olanı dönüyor.
    /// </summary>
    /// <exception cref="WalletRejectedException">wallet-api isteği reddetti.</exception>
    /// <exception cref="UpstreamUnavailableException">wallet-api cevap vermedi.</exception>
    public async Task<PlacedHold> PlaceAsync(Domain.CardTopup topup, CancellationToken ct)
    {
        var request = new
        {
            HoldId = topup.Id,
            topup.WalletId,
            topup.Amount,
            topup.Currency,
            topup.Provider
        };

        HttpResponseMessage response;

        try
        {
            response = await http.PostAsJsonAsync("v1/card-topup-holds", request, JsonOptions, ct);
        }
        catch (Exception exception) when (Upstream.IsNoAnswer(exception, ct))
        {
            throw new UpstreamUnavailableException("wallet-api", exception);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500)
            {
                throw new UpstreamUnavailableException("wallet-api", innerException: null);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new WalletRejectedException(
                    response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(),
                    await response.Content.ReadAsByteArrayAsync(ct));
            }

            return await response.Content.ReadFromJsonAsync<PlacedHold>(JsonOptions, ct)
                   ?? throw new UpstreamUnavailableException("wallet-api", innerException: null);
        }
    }
}

/// <param name="AccountId">Cüzdanın hesabı.</param>
public sealed record PlacedHold(Guid HoldId, Guid AccountId, Guid WalletId, decimal Amount, string Currency, bool Replayed);

/// <summary>
/// İsteğin token'ını iletir. Token doğrulanmış kimliğin kaydından okunuyor, gelen başlıktan
/// değil (ön API'lerdeki kalıp).
/// </summary>
internal sealed class ForwardAccessTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (accessor.HttpContext is { } context
            && await context.GetTokenAsync("access_token") is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

internal static class Upstream
{
    /// <summary>
    /// "Cevap alamadık" sayılan istisnalar. Resilience pipeline'ı denemeleri tükettiğinde
    /// kendi istisnasını atıyor; servisin kendi kapanışı (<paramref name="ct"/>) bunların dışında.
    /// </summary>
    public static bool IsNoAnswer(Exception exception, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && exception is HttpRequestException
            or TaskCanceledException
            or TimeoutRejectedException
            or BrokenCircuitException;

    /// <summary>5xx, 429 ve 408: karşı taraf "şu an olmaz" diyor, kalıcı bir cevap değil.</summary>
    public static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout;
}
