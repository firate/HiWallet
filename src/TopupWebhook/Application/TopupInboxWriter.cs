using System.Text.Json;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.TopupWebhook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.TopupWebhook.Application;

/// <summary>
/// Kabul edilen webhook'u inbox'a yazar. 200 dönmeden önceki SON adım —
/// kalıcılık garanti olmadan sağlayıcıya başarı denmiyor (overview.md madde 5).
/// </summary>
public sealed class TopupInboxWriter(
    IDbContextFactory<InboxDbContext> contextFactory,
    TimeProvider timeProvider)
{
    internal static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    /// <returns>
    /// Aynı <c>(provider, event_id)</c> daha önce kaydedilmişse <c>true</c>.
    /// Çağıran yine 200 dönüyor: sağlayıcı için tekrar gönderim başarılı bir
    /// sonuçtur, hata değil.
    /// </returns>
    public async Task<bool> WriteAsync(TopupReceived message, string rawPayload, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var payload = JsonSerializer.Serialize(message, PayloadJsonOptions);

        // "Önce SELECT sonra INSERT" YOK (CLAUDE.md): iki eşzamanlı istek arasında
        // TOCTOU açığı var ve webhook'lar tam da paralel gelir.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO topup_inbox
                 (id, provider, event_id, ledger_account_id, payload, raw_payload, received_at, publish_attempts)
             VALUES
                 ({Guid.NewGuid()}, {message.Provider}, {message.EventId}, {message.LedgerAccountId},
                  {payload}::jsonb, {rawPayload}::jsonb, {timeProvider.GetUtcNow()}, 0)
             ON CONFLICT (provider, event_id) DO NOTHING
             """,
            ct);

        return inserted == 0;
    }
}
