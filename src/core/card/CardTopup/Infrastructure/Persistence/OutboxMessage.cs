using System.Text.Json;
using HiWallet.Shared.Contracts.CardTopups;

namespace HiWallet.CardTopup.Infrastructure.Persistence;

/// <summary>
/// Wallet'a gönderilmeyi bekleyen kapanış. Yüklemenin son durumuyla AYNI transaction'da
/// yazılıyor; broker'a taşımak relay'in işi (çekim orchestrator'ının kalıbı, decisions.md
/// madde 32). Ayrı commit olsaydı aradaki çökme ya "yükleme ödendi ama para cüzdana hiç
/// gitmedi" ya da tersini bırakırdı.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Mesajın kimliği; broker'da <c>MessageId</c>.</summary>
    public required Guid Id { get; init; }

    public required Guid CardTopupId { get; init; }

    /// <summary>Yayınlanacak <c>CardTopupClosed</c>; relay'de yeniden serialize EDİLMİYOR.</summary>
    public required string Payload { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int PublishAttempts { get; set; }

    public string? LastError { get; set; }

    public static OutboxMessage For(CardTopupClosed message, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CardTopupId = message.CardTopupId,
        Payload = JsonSerializer.Serialize(message, PayloadJsonOptions),
        CreatedAt = now
    };

    /// <summary>Tüketicinin okuduğu biçimle aynı (camelCase); ayrışırsa alanlar boş gelir.</summary>
    internal static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);
}
