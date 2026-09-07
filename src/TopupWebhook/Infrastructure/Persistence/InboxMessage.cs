namespace HiWallet.TopupWebhook.Infrastructure.Persistence;

/// <summary>
/// Kabul edilmiş bir webhook. Broker'a gitmeden ÖNCE buraya yazılıyor ve 200 ancak
/// commit'ten sonra dönülüyor (overview.md madde 5).
///
/// Inbox'ın varlık sebebi: sağlayıcıya "aldım" demeden önce mesajın kalıcı olması.
/// Doğrudan RabbitMQ'ya publish edilip 200 dönseydi, broker o an erişilemez olduğunda
/// ya sağlayıcıya hata dönmek (sağlayıcı belki hiç tekrar denemez) ya da kaybetmek
/// kalırdı. Şimdi HTTP yolu yalnızca Postgres'e bağımlı; broker'a taşımak relay'in işi.
/// </summary>
public sealed class InboxMessage
{
    public required Guid Id { get; init; }

    public required string Provider { get; init; }

    /// <summary>Sağlayıcının event id'si. <c>(provider, event_id)</c> UNIQUE.</summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Mesajın hangi akışa ait olduğu: <c>topup</c> ya da <c>settlement</c>. Relay
    /// hangi exchange'e yayınlayacağını buradan biliyor.
    ///
    /// İkinci bir inbox tablosu ve ikinci bir relay AÇILMADI: relay'in işi (ele
    /// geçir, yayınla, işaretle) iki akışta birebir aynı ve kopyalansaydı publisher
    /// confirms, SKIP LOCKED ve yayınla-sonra-işaretle sırası iki yerde ayrı ayrı
    /// doğru tutulmak zorunda kalırdı (decisions.md madde 25).
    /// </summary>
    public required InboxKind Kind { get; init; }

    /// <summary>
    /// Yayınlanırken kullanılacak routing key. Payload'ın içinden de çıkarılabilirdi
    /// ama kolon olarak duruyor: relay her satır için JSON parse etmek zorunda
    /// kalmasın (overview.md madde 8).
    ///
    /// Top-up'ta cüzdan kimliği — partition anahtarı, aynı cüzdanın mesajları aynı
    /// kuyruğa düşsün diye. Settlement'ta sabit mesaj tipi: hiçbir cüzdana
    /// dokunmuyor, partition'ın koruduğu şey orada yok.
    /// </summary>
    public required string RoutingKey { get; init; }

    /// <summary>
    /// Yayınlanacak <c>TopupReceived</c>'in JSON hali. Ham gövde değil, NORMALİZE
    /// edilmiş mesaj: kalıcı olan şeyle broker'a giden şey birebir aynı olsun diye.
    /// Relay'de yeniden dönüştürme yapılsaydı, o dönüşümün hatası inbox'a
    /// yazıldıktan sonra ortaya çıkardı.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    /// Sağlayıcının gönderdiği ham gövde. İşleme girmiyor; ihtilafta "bize tam olarak
    /// ne geldi" sorusunun tek cevabı bu — imza da bu baytlar üzerinde doğrulandı.
    /// </summary>
    public required string RawPayload { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Broker'a başarıyla verildiği an. NULL ise relay'in işi bitmemiş.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Kaç kez denendi. Sürekli artıyorsa alarm konusu.</summary>
    public int PublishAttempts { get; set; }

    /// <summary>Son denemenin hatası. Yalnızca teşhis için.</summary>
    public string? LastError { get; set; }
}
