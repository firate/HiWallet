using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace HiWallet.EdgeApi.InternalServices;

/// <summary>
/// İstemcinin token'ını iç servise AYNEN iletir. İç servis onu yeniden doğruluyor ve
/// aktörü ondan okuyor; ön API kimlik üretmiyor, yalnızca taşıyor.
///
/// Token doğrulanmış kimliğin kaydından okunuyor, gelen başlıktan değil: mobil
/// istemcide başlıktan, tarayıcının oturumunda sunucudaki oturumdan geliyor ve iki
/// yol da aynı yere yazıyor.
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
