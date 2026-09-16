using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HiWallet.Fakes.Topups;

/// <summary>
/// Sağlayıcı kimliğiyle <c>topup-webhook</c>'a webhook gönderir.
///
/// <b>İmza HAM gövde baytları üzerinde.</b> Nesneyi yeniden serialize edip
/// imzalasaydık boşluk ve alan sırası farkı doğrulamayı düşürürdü — bizim
/// doğrulayıcımız da tam olarak gelen baytlara bakıyor.
///
/// <b>Bu sınıf iki sahte servis tarafından paylaşılıyor</b> ve gerekçesi
/// <c>Fakes.Core.csproj</c>'da: sözleşmeyi biz dayatıyoruz, ayrışacak iki taraf yok.
/// </summary>
public sealed class TopupWebhookSender(
    IHttpClientFactory httpClientFactory,
    IOptions<FakeProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<TopupWebhookSender> logger)
{
    public const string HttpClientName = "topup-webhook";

    /// <summary>Bizim sözleşmemizin imza başlığı. Bankanın callback'indeki başlıkla ilgisi yok.</summary>
    private const string SignatureHeader = "X-Hive-Signature";

    private readonly FakeProviderOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// İstenen modda event'leri üretir ve sırayla gönderir.
    /// </summary>
    /// <returns>Gönderilen <c>eventId</c>'ler — testler bunlarla inbox'a bakıyor.</returns>
    public async Task<IReadOnlyList<string>> SendAsync(TopupDelivery delivery, CancellationToken ct)
    {
        var events = Build(delivery);

        if (delivery.Mode is TopupDeliveryMode.Delayed)
        {
            // Gecikme BEKLEYEREK uygulanıyor, zamanlanarak değil: çağıran bu
            // metodu zaten arka planda koşturuyor ve gecikmenin gözlemlenebilir
            // olması gerekiyor. Süreç bu sırada ölürse event kaybolur — kabul
            // edilebilir, çünkü tetikleyen taraf yeniden tetikleyebilir. Bankanın
            // senaryo tablosundan farkı bu: orada zamanlamayı BİZ değil adaptör
            // sürüyordu, o yüzden kalıcı olmak zorundaydı.
            await Task.Delay(TimeSpan.FromMilliseconds(delivery.DelayMilliseconds), timeProvider, ct);
        }

        foreach (var payload in events)
        {
            ct.ThrowIfCancellationRequested();

            await PostAsync(payload, ct);
        }

        return events.Select(e => e.EventId).ToList();
    }

    /// <summary>
    /// Mod başına event listesi. <b>Liste GÖNDERİM sırasında</b> — <c>OutOfOrder</c>
    /// zaten burada ters çevriliyor, çağıran sıraya karışmıyor.
    /// </summary>
    private IReadOnlyList<TopupWebhookBody> Build(TopupDelivery delivery)
    {
        var now = timeProvider.GetUtcNow();

        switch (delivery.Mode)
        {
            case TopupDeliveryMode.Duplicate:
            {
                // AYNI event iki kez: eventId de aynı. Farklı olsaydı iki ayrı
                // para girişi olurdu ve sınanan şey idempotency değil, toplama.
                var single = NewBody(delivery, now, index: 0);

                return [single, single];
            }

            case TopupDeliveryMode.OutOfOrder:
            {
                var count = Math.Max(2, delivery.Count);

                var ordered = Enumerable.Range(0, count)
                    .Select(i => NewBody(delivery, now.AddSeconds(i), i))
                    .ToList();

                // Ters çevriliyor: en YENİ olan önce gidiyor.
                ordered.Reverse();

                return ordered;
            }

            default:
                return [NewBody(delivery, now, index: 0)];
        }
    }

    private TopupWebhookBody NewBody(TopupDelivery delivery, DateTimeOffset occurredAt, int index) =>
        new()
        {
            // Sağlayıcının event kimliği. Tekilliği (provider, event_id) üzerinde,
            // o yüzden sağlayıcı adı ön eke GEREK YOK — inbox zaten sağlayıcıyı
            // ayrı kolonda tutuyor.
            EventId = $"evt-{Guid.NewGuid():N}",
            WalletId = delivery.WalletId,
            Amount = delivery.Amount,
            Currency = delivery.Currency,
            Reference = $"{_options.Provider}-ref-{index}-{Guid.NewGuid().ToString("N")[..8]}",
            OccurredAt = occurredAt
        };

    private async Task PostAsync(TopupWebhookBody body, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/webhooks/topup/{_options.Provider}")
        {
            Content = content
        };

        request.Headers.TryAddWithoutValidation(SignatureHeader, Sign(bytes));

        var client = httpClientFactory.CreateClient(HttpClientName);

        using var response = await client.SendAsync(request, ct);

        // Ret durum kodu İSTİSNA DEĞİL: bir event'in reddedilmesi (401, 400) kalan
        // event'leri göndermeyi engellememeli — sahte sağlayıcının işi göndermek,
        // sonucu yorumlamak değil. Bağlantı hatası ise istisna olarak yukarı
        // çıkıyor ve dizinin geri kalanı gönderilmiyor: karşı uç yoksa sıradaki
        // istekler de aynı hatayı alırdı.
        logger.LogInformation(
            "Top-up webhook gönderildi. {Provider}/{EventId} → {Status}",
            _options.Provider, body.EventId, (int)response.StatusCode);
    }

    private string Sign(ReadOnlySpan<byte> body)
    {
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.WebhookSecret!), body, hash);

        return "sha256=" + Convert.ToHexStringLower(hash);
    }
}

/// <summary>
/// <c>topup-webhook</c>'un beklediği gövde. Alan adları onun
/// <c>TopupWebhookPayload</c>'ıyla eşleşmek zorunda; ayrışırsa doğrulama "eksik
/// alan" der ve sebebi burada aranmaz.
/// </summary>
internal sealed record TopupWebhookBody
{
    public required string EventId { get; init; }

    public required Guid WalletId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string Reference { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
