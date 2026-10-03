using HiWallet.Shared.Infrastructure.Authentication;

namespace HiWallet.StaffAdmin.Domain;

/// <summary>
/// Panelde tanımlanan rol: kodun izinlerinden bir set. Rolün izinleri değişince o roldeki
/// herkesin yetkisi bir sonraki istekte değişiyor. Ad sonradan değişmiyor.
/// </summary>
public sealed class StaffRole
{
    private StaffRole()
    {
        // EF Core materialization.
    }

    /// <summary>Zamana göre sıralı (UUID v7).</summary>
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Adın büyük harfli hali; ad büyük-küçük harf farkıyla ikinci kez alınamıyor.</summary>
    public string NormalizedName { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>Tekrarsız ve kodun sırasıyla: kayıt ve panel hep aynı sırayı görüyor.</summary>
    public List<string> Permissions { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public static StaffRole Create(
        string name, string? description, IReadOnlyCollection<string> permissions, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = name,
        NormalizedName = NormalizeName(name),
        Description = description,
        Permissions = InCodeOrder(permissions),
        CreatedAt = now
    };

    public static string NormalizeName(string name) => name.ToUpperInvariant();

    public void Change(string? description, IReadOnlyCollection<string> permissions)
    {
        Description = description;
        Permissions = InCodeOrder(permissions);
    }

    private static List<string> InCodeOrder(IReadOnlyCollection<string> permissions) =>
        [.. StaffPermissions.All.Where(permissions.Contains)];
}
