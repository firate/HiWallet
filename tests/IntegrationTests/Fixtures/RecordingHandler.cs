using System.Net;
using System.Text;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// İç servisin yerinde: her isteğe aynı cevabı veriyor ve son isteğin yolunu tutuyor. Ön
/// API'nin isteği doğru yere ilettiğini ve reddi aynen döndüğünü sınamak için.
/// </summary>
internal sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public string? LastPath { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastPath = request.RequestUri?.AbsolutePath;

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json")
        });
    }
}
