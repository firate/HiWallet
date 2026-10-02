using System.Text.Json;
using HiWallet.StaffAdmin.Domain;
using HiWallet.StaffAdmin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Değişikliğin kaydı, değişiklik kimlik sağlayıcıya uygulandıktan SONRA yazılıyor:
/// uygulanmamış bir değişiklik kayıtta görünmesin. İki sistem arasında ortak transaction
/// yok; kayıt yazılamazsa değişiklik bütün ayrıntısıyla kritik seviyede log'a düşüyor ve
/// istek hata dönüyor.
/// </summary>
public sealed class StaffAudit(
    IDbContextFactory<StaffAdminDbContext> contexts,
    TimeProvider time,
    ILogger<StaffAudit> logger)
{
    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(
        Actor actor,
        StaffAuditAction action,
        StaffAuditTarget targetType,
        Guid targetId,
        string targetLabel,
        object details,
        CancellationToken ct)
    {
        var entry = StaffAuditEvent.Record(
            actor, action, targetType, targetId, targetLabel,
            JsonSerializer.Serialize(details, DetailsJson),
            time.GetUtcNow());

        try
        {
            // Değişiklik uygulandı; istemci bağlantıyı kesse de kaydı yazılmalı.
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            db.AuditEvents.Add(entry);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Personel değişikliği kimlik sağlayıcıya uygulandı ama kaydı yazılamadı: {Action} {TargetType} {TargetId} {TargetLabel} yapan {ActorSubject}, ayrıntı {Details}",
                StaffAuditTexts.Actions[action], StaffAuditTexts.Targets[targetType], targetId, targetLabel,
                actor.Subject, entry.Details);
            throw;
        }
    }
}
