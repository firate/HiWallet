using System.Net.Http.Headers;

namespace HiWallet.WalletConsumer.Identity;

/// <summary>İsteğe servisin kendi token'ını ekliyor.</summary>
internal sealed class ServiceTokenHandler(ServiceTokens tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
