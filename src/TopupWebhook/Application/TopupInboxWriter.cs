using System.Text.Json;
using HiWallet.Shared.Contracts.Settlements;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.TopupWebhook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.TopupWebhook.Application;

/// <summary>
/// Kabul edilen webhook'u inbox'a yazar. 202 dönmeden önceki SON adım —
/// kalıcılık garanti olmadan sağlayıcıya başarı denmiyor (overview.md madde 5).
///
/// İki akış da buradan geçiyor (top-up, settlement). Ayrı yazıcılar olsaydı
/// <c>ON CONFLICT DO NOTHING</c> kalıbı ve "0 satır ise tekrar" yorumu iki yerde
/// ayrı ayrı doğru tutulmak zorunda kalırdı.
/// </summary>
public sealed class TopupInboxWriter(
    IDbContextFactory<InboxDbContext> contextFactory,
    TimeProvider timeProvider)
{
    internal static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    /// <returns>
    /// Aynı <c>(provider, event_id)</c> daha önce kaydedilmişse <c>true</c>.
    /// Çağıran yine 202 dönüyor: sağlayıcı için tekrar gönderim başarılı bir
    /// sonuçtur, hata değil.
    /// </returns>
    public Task<bool> WriteAsync(TopupReceived message, string rawPayload, CancellationToken ct) =>
        WriteAsync(
            InboxKind.Topup,
            message.Provider,
            message.EventId,
            // Partition anahtarı cüzdan kimliği: aynı cüzdanın mesajları aynı
            // kuyruğa düşsün (overview.md madde 8).
            routingKey: message.LedgerAccountId.ToString(),
            message,
            rawPayload,
            ct);

    public Task<bool> WriteAsync(SettlementReceived message, string rawPayload, CancellationToken ct) =>
        WriteAsync(
            InboxKind.Settlement,
            message.Provider,
            message.SettlementId,
            // Sabit: settlement hiçbir cüzdana dokunmuyor, partition'ın koruduğu
            // sıra burada yok.
            routingKey: SettlementTopology.RoutingKey,
            message,
            rawPayload,
            ct);

    /// <summary>
    /// Fatura da settlement ile AYNI exchange'e gidiyor, ayrı routing key ile
    /// (SettlementTopology). Inbox açısından ikisi de "sağlayıcıdan gelen, cüzdana
    /// dokunmayan bildirim" — aynı kind altında duruyorlar.
    /// </summary>
    public Task<bool> WriteAsync(
        ProviderInvoiceReceived message, string rawPayload, CancellationToken ct) =>
        WriteAsync(
            InboxKind.Settlement,
            message.Provider,
            // Ön ek ZORUNLU: inbox tekilliği (provider, event_id) ve fatura numarası
            // ile settlement id'si aynı alanı paylaşıyor. Ön eksiz, aynı sağlayıcının
            // "st_42" batch'i ile "st_42" faturası birbirini yutardı.
            eventId: $"invoice:{message.InvoiceRef}",
            routingKey: SettlementTopology.InvoiceRoutingKey,
            message,
            rawPayload,
            ct);

    private async Task<bool> WriteAsync<T>(
        InboxKind kind,
        string provider,
        string eventId,
        string routingKey,
        T message,
        string rawPayload,
        CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var payload = JsonSerializer.Serialize(message, PayloadJsonOptions);

        // "Önce SELECT sonra INSERT" YOK (CLAUDE.md): iki eşzamanlı istek arasında
        // TOCTOU açığı var ve webhook'lar tam da paralel gelir.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO topup_inbox
                 (id, provider, event_id, kind, routing_key, payload, raw_payload, received_at, publish_attempts)
             VALUES
                 ({Guid.NewGuid()}, {provider}, {eventId}, {kind.ToText()}, {routingKey},
                  {payload}::jsonb, {rawPayload}::jsonb, {timeProvider.GetUtcNow()}, 0)
             ON CONFLICT (provider, event_id) DO NOTHING
             """,
            ct);

        return inserted == 0;
    }
}
