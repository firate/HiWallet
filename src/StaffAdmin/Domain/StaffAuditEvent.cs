namespace HiWallet.StaffAdmin.Domain;

/// <summary>
/// Personel yönetiminde yapılan bir değişikliğin kaydı: kim, ne zaman, kime, ne yaptı.
/// Kimlik sağlayıcının kendi kaydı işi servis hesabının yaptığını görüyor; işi yapan
/// çalışan yalnızca burada. Satır değişmiyor ve silinmiyor (REVOKE).
/// </summary>
public sealed class StaffAuditEvent
{
    private StaffAuditEvent()
    {
        // EF Core materialization.
    }

    /// <summary>Zamana göre sıralı (UUID v7): sayfalama kimlikle yapılıyor.</summary>
    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Değişikliği yapan çalışanın kimliği (<c>sub</c>); kurulumda <see cref="Actor.System"/>.</summary>
    public string ActorSubject { get; private set; } = null!;

    /// <summary>Kaydın okunması için; kimlik <see cref="ActorSubject"/>.</summary>
    public string? ActorName { get; private set; }

    public StaffAuditAction Action { get; private set; }

    public StaffAuditTarget TargetType { get; private set; }

    public Guid TargetId { get; private set; }

    /// <summary>Rolün adı ya da çalışanın e-postası: hedef sonradan silinse de kayıt okunabilsin.</summary>
    public string TargetLabel { get; private set; } = null!;

    /// <summary>Değişikliğin ayrıntısı, JSON: verilen izinler, önceki ve sonraki roller.</summary>
    public string Details { get; private set; } = null!;

    public static StaffAuditEvent Record(
        Actor actor,
        StaffAuditAction action,
        StaffAuditTarget targetType,
        Guid targetId,
        string targetLabel,
        string details,
        DateTimeOffset occurredAt) => new()
    {
        Id = Guid.CreateVersion7(occurredAt),
        OccurredAt = occurredAt,
        ActorSubject = actor.Subject,
        ActorName = actor.Name,
        Action = action,
        TargetType = targetType,
        TargetId = targetId,
        TargetLabel = targetLabel,
        Details = details
    };
}

public enum StaffAuditAction
{
    RoleCreated,
    RoleUpdated,
    RoleDeleted,
    StaffInvited,
    StaffRolesChanged,
    StaffDisabled,
    StaffEnabled,
    InvitationSent
}

public enum StaffAuditTarget
{
    Role,
    Staff
}

/// <summary>Değişikliği yapan: token'daki çalışan ya da kurulum.</summary>
public sealed record Actor(string Subject, string? Name)
{
    /// <summary>Servisin açılışta kendi yaptığı kurulum (izinler, ilk yönetici).</summary>
    public static readonly Actor System = new("system", "Kurulum");

    /// <summary>Çalışanın kimlik sağlayıcıdaki kimliği; kurulumda yok.</summary>
    public Guid? Id => Guid.TryParse(Subject, out var id) ? id : null;
}
