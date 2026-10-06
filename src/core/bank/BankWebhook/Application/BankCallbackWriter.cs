using HiWallet.BankIntegration.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.BankWebhook.Application;

/// <summary>
/// Doğrulanmış callback'i inbox'a yazar. <c>202</c> dönmeden önceki SON adım —
/// kalıcılık garanti olmadan bankaya "aldım" denmiyor (decisions.md madde 29, 35).
///
/// <b>Gövde ÇÖZÜMLENMİYOR.</b> Ham hali olduğu gibi yazılıyor ve anlamlandırmayı
/// <c>bank-adapter</c>'ın relay'i yapıyor. Burada çözümlenseydi bozuk bir gövde
/// bankaya <c>400</c> olarak dönerdi; banka da sınırlı sayıda deneyip vazgeçtiği
/// için o sonuç kaybolurdu. Şimdi satır inbox'ta duruyor, işlenemiyorsa alarm
/// üretiyor ve mutabakat taraması zaten yedekte.
/// </summary>
public sealed class BankCallbackWriter(
    IDbContextFactory<BankDbContext> contextFactory,
    TimeProvider timeProvider)
{
    /// <returns>
    /// Aynı <c>(provider, event_id)</c> daha önce kaydedilmişse <c>true</c>.
    /// Çağıran yine <c>202</c> dönüyor: banka için yeniden gönderim başarılı bir
    /// sonuçtur, hata değil.
    /// </returns>
    public async Task<bool> WriteAsync(
        string provider, string eventId, string rawPayload, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // "Önce SELECT sonra INSERT" YOK (CLAUDE.md "Idempotency"): iki eşzamanlı
        // request arasında TOCTOU açığı var ve callback'ler tam da paralel gelir.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO bank_callbacks
                 (id, provider, event_id, raw_payload, received_at, process_attempts)
             VALUES
                 ({Guid.NewGuid()}, {provider}, {eventId}, {rawPayload}, {timeProvider.GetUtcNow()}, 0)
             ON CONFLICT (provider, event_id) DO NOTHING
             """,
            ct);

        return inserted == 0;
    }
}
