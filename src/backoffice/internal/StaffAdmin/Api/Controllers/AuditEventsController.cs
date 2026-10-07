using System.Text.Json;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Domain;
using HiWallet.StaffAdmin.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.StaffAdmin.Api.Controllers;

/// <summary>Personel yönetimindeki değişikliklerin kaydı: kim, ne zaman, kime, ne yaptı.</summary>
[ApiController]
[Route("v1/audit-events")]
[Authorize(Policy = HiWalletPolicies.StaffManage)]
public sealed class AuditEventsController(IDbContextFactory<StaffAdminDbContext> contexts) : ControllerBase
{
    /// <summary>Yeniden eskiye. Sayfalama cursor ile; kimlikler zamana göre sıralı.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    public async Task<AuditEventsResponse> List([FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct)
    {
        var take = Paging.Size(size);
        await using var db = await contexts.CreateDbContextAsync(ct);

        var query = db.AuditEvents.AsNoTracking();

        if (after is { } cursor)
        {
            query = query.Where(e => e.Id.CompareTo(cursor) < 0);
        }

        var rows = await query.OrderByDescending(e => e.Id).Take(take + 1).ToListAsync(ct);
        var items = rows.Take(take).Select(ToResponse).ToList();

        return new AuditEventsResponse(items, take, rows.Count > take ? items[^1].EventId : null);
    }

    private static AuditEventResponse ToResponse(StaffAuditEvent e) => new(
        e.Id,
        e.OccurredAt,
        e.ActorSubject,
        e.ActorName,
        StaffAuditTexts.Actions[e.Action],
        StaffAuditTexts.Targets[e.TargetType],
        e.TargetId,
        e.TargetLabel,
        JsonDocument.Parse(e.Details).RootElement.Clone());
}
