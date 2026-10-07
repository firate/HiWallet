namespace HiWallet.StaffAdmin.Domain;

/// <summary>
/// Çalışan. Kimliği kimlik sağlayıcıdaki kullanıcının kimliği, yani token'daki <c>sub</c>.
/// Parola, OTP ve oturum kimlik sağlayıcıda; çalışanın açık olup olmadığı ve hangi
/// rollerde olduğu burada. Çalışan yalnızca panelden açılıyor: kimlik sağlayıcının
/// konsolunda açılan kullanıcı burada yok ve hiçbir izni yok.
/// </summary>
public sealed class StaffMember
{
    private StaffMember()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    /// <summary>Küçük harfle: aynı adres büyük harfle ikinci kez davet edilemesin.</summary>
    public string Email { get; private set; } = null!;

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    /// <summary>Kapatılan çalışanın hiçbir izni yok; kimlik sağlayıcıda girişi de kapalı.</summary>
    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Çalışanın geçerli bir token'la ilk görüldüğü an: parolasını ve OTP'sini kurmuş ve
    /// giriş yapmış. Boşsa davet bekliyor.
    /// </summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    public static StaffMember Invite(
        Guid id, string email, string? firstName, string? lastName, DateTimeOffset now) => new()
    {
        Id = id,
        Email = NormalizeEmail(email),
        FirstName = firstName,
        LastName = lastName,
        Enabled = true,
        CreatedAt = now
    };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetEnabled(bool enabled) => Enabled = enabled;

    /// <returns>Çalışan ilk kez görüldüyse <c>true</c>; kaydedilmesi gerekiyor.</returns>
    public bool MarkSeen(DateTimeOffset now)
    {
        if (ActivatedAt is not null)
        {
            return false;
        }

        ActivatedAt = now;
        return true;
    }
}
