using System.Net.Http.Headers;
using HiWallet.Onboarding.Application.Abstractions;

namespace HiWallet.Onboarding.Infrastructure;

/// <summary>İsteğe servisin kendi token'ını ekliyor: kimlik sağlayıcının yönetim API'si ve wallet-api.</summary>
internal sealed class ServiceTokenHandler(IServiceTokens tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
