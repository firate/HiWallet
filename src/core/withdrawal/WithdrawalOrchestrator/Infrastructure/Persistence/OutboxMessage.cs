using System.Text.Json;
using HiWallet.Shared.Infrastructure.Messaging;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;

/// <summary>
/// Gönderilmeyi bekleyen bir komut. Saga geçişiyle AYNI transaction'da yazılıyor,
/// broker'a taşımak relay'in işi (decisions.md madde 32).
///
/// Varlık sebebi: saga'nın her adımı iki şey yapıyor — durumu ilerlet ve komut
/// gönder. Bu ikisi atomik olmazsa aradaki çökme ya "komut gitti, saga ilerlemedi"
/// ya da daha sinsi olanı bırakıyor: saga <c>BankTransferPending</c> ama bankaya hiç
/// komut gitmemiş, müşterinin parası clearing'de asılı ve ortada tek bir hata log'u
/// yok. Outbox ikisini tek commit'e koyuyor.
///
/// <b>Inbox ile karıştırılmamalı.</b> <c>topup_inbox</c> "dışarıdan gelen mesajı,
/// göndereni onaylamadan önce kalıcı yaz". Bu tablo tam tersi yön: "kendi işini
/// commit'lerken haber vermeyi de aynı commit'e al".
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>
    /// Mesajın kimliği ve AYNI ZAMANDA komutun <c>CommandId</c>'si — iki ayrı alan
    /// tutulmuyor, bilerek. Relay aynı satırı iki kez yayınlarsa (publish edip commit
    /// edemediğinde olur) alıcıya giden <c>CommandId</c> de aynı oluyor ve karşı
    /// taraftaki <c>processed_messages</c> tekrarı yutuyor. Ayrı bir yüzey id
    /// üretilseydi bu bağ elle kurulmak zorunda kalır, ilk unutulduğunda çift
    /// işleme dönerdi.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>Hangi saga ürettiyse o. Teşhiste "bu saga ne göndermiş" sorgusu için.</summary>
    public required Guid SagaId { get; init; }

    /// <summary>
    /// Routing key, yani mesaj tipinin adı
    /// (<see cref="Shared.Infrastructure.Messaging.WithdrawalTopology.RoutingKeyFor{T}"/>).
    /// Kolon olarak duruyor ki relay her satır için JSON parse etmek zorunda kalmasın.
    /// Exchange kolonu YOK: orchestrator'ın yayınladığı her şey tek exchange'e gidiyor.
    /// </summary>
    public required string RoutingKey { get; init; }

    /// <summary>
    /// Yayınlanacak komutun JSON hali. Relay'de yeniden serialize EDİLMİYOR —
    /// dönüşüm burada bir kez yapılıp kalıcı hale geliyor, böylece commit'ten sonra
    /// ortaya çıkabilecek bir serialize hatası kalmıyor.
    /// </summary>
    public required string Payload { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Broker'a başarıyla verildiği an. NULL ise relay'in işi bitmemiş.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Kaç kez denendi. Sürekli artıyorsa alarm konusu.</summary>
    public int PublishAttempts { get; set; }

    /// <summary>Son denemenin hatası. Yalnızca teşhis için.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Yayınlanacak komuttan satır üretir.
    ///
    /// Komutu ÇAĞIRAN yazmıyor, bu metot ürettiriyor: id önce oluşuyor ve komuta o
    /// veriliyor. Böylece <see cref="Id"/> ile komutun <c>CommandId</c>'sinin
    /// ayrışması mümkün değil — ayrışsalardı relay'in ikinci teslimi alıcı tarafta
    /// yeni bir komut gibi görünür ve çift işlenirdi.
    /// </summary>
    /// <param name="command">Kendisine verilen id'yi <c>CommandId</c> olarak kullanmalı.</param>
    public static OutboxMessage For<T>(Guid sagaId, Func<Guid, T> command, DateTimeOffset now)
    {
        var commandId = Guid.NewGuid();

        return new OutboxMessage
        {
            Id = commandId,
            SagaId = sagaId,
            RoutingKey = WithdrawalTopology.RoutingKeyFor<T>(),
            Payload = JsonSerializer.Serialize(command(commandId), PayloadJsonOptions),
            CreatedAt = now
        };
    }

    /// <summary>
    /// Tüketicilerin okuduğu biçimle AYNI olmak zorunda
    /// (<c>JsonSerializerDefaults.Web</c>, yani camelCase). Ayrışırsa mesaj
    /// karşı tarafta boş alanlarla deserialize olur ve hata vermez.
    /// </summary>
    internal static readonly JsonSerializerOptions PayloadJsonOptions =
        new(JsonSerializerDefaults.Web);
}
