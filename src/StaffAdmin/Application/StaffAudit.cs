using System.Text.Json;
using HiWallet.StaffAdmin.Domain;
using HiWallet.StaffAdmin.Infrastructure.Persistence;

namespace HiWallet.StaffAdmin.Application;

/// <summary>
/// Değişikliğin kaydı, değişiklikle AYNI transaction'da: kayıt yazılamazsa değişiklik de
/// yazılmıyor. Kayıt bağlama ekleniyor; kaydetmek değişikliği yapanın işi.
/// </summary>
public sealed class StaffAudit(TimeProvider time)
{
    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    public void Add(
        StaffAdminDbContext db,
        Actor actor,
        StaffAuditAction action,
        StaffAuditTarget targetType,
        Guid targetId,
        string targetLabel,
        object details) =>
        db.AuditEvents.Add(StaffAuditEvent.Record(
            actor, action, targetType, targetId, targetLabel,
            JsonSerializer.Serialize(details, DetailsJson),
            time.GetUtcNow()));
}
