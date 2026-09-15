namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Bir <c>WebApplicationFactory</c> istemcisine yönlendiren handler.
///
/// İki in-memory host arasında gerçek soket açılamaz, ama HTTP katmanının geri
/// kalanı — serileştirme, başlıklar, durum kodları, imza doğrulaması — olduğu gibi
/// koşuyor. Yani sınanan şey "iki sınıf birbirini çağırabiliyor mu" değil, "iki
/// servis HTTP üzerinden anlaşabiliyor mu".
///
/// Sözleşme uyuşmazlığı burada yakalanıyor: alan adı değişse, durum kodu kaysa ya
/// da imza şeması ayrışsa test düşüyor. Doğrudan metot çağrısıyla kurulmuş bir
/// sahte bunların hiçbirini göremezdi.
/// </summary>
internal sealed class PassthroughHandler(HttpClient inner) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return await inner.SendAsync(clone, cancellationToken);
    }
}
