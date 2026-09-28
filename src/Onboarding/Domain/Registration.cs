namespace HiWallet.Onboarding.Domain;

/// <summary>
/// Kayıt: e-posta, adrese giden kod, kimlik sağlayıcıdaki kullanıcı ve wallet'taki hesap.
/// Sıra sabit: e-posta doğrulanmadan kullanıcı açılmıyor, kullanıcı açılmadan hesap.
///
/// Parola burada DURMUYOR. E-posta parola sorulmadan önce doğrulandığı için parola
/// yalnızca kullanıcıyı açan istekte geçiyor ve kimlik sağlayıcıya yazılıyor.
/// </summary>
public sealed class Registration
{
    /// <summary>Kodun geçerlilik süresi.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>E-posta doğrulandıktan sonra kaydın tamamlanabileceği süre.</summary>
    public static readonly TimeSpan CompletionWindow = TimeSpan.FromMinutes(30);

    /// <summary>Yanlış deneme sınırı. Altı haneli kod bir milyon ihtimal.</summary>
    public const int MaxAttempts = 5;

    private Registration()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    /// <summary>Küçük harfe indirilmiş adres. Kimlik sağlayıcıda kullanıcı adı da bu.</summary>
    public string Email { get; private set; } = string.Empty;

    public string CodeHash { get; private set; } = string.Empty;

    public DateTimeOffset CodeExpiresAt { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    /// <summary>Kimlik sağlayıcıda açılan kullanıcının <c>sub</c>'ı.</summary>
    public string? Subject { get; private set; }

    /// <summary>Wallet'ta açılan bireysel hesap.</summary>
    public Guid? AccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static Registration Start(Guid id, string email, string code, DateTimeOffset now) => new()
    {
        Id = id,
        Email = NormalizeEmail(email),
        CodeHash = VerificationCode.Hash(id, code),
        CodeExpiresAt = now + CodeLifetime,
        CreatedAt = now
    };

    public CodeCheck VerifyEmail(string code, DateTimeOffset now)
    {
        if (EmailVerifiedAt is not null) return CodeCheck.AlreadyVerified;
        if (FailedAttempts >= MaxAttempts) return CodeCheck.Locked;
        if (now > CodeExpiresAt) return CodeCheck.Expired;

        if (!VerificationCode.Matches(Id, code, CodeHash))
        {
            FailedAttempts++;
            return CodeCheck.WrongCode;
        }

        EmailVerifiedAt = now;
        return CodeCheck.Verified;
    }

    public bool CanComplete(DateTimeOffset now) =>
        EmailVerifiedAt is { } verifiedAt && now <= verifiedAt + CompletionWindow;

    /// <summary>Kimlik sağlayıcıda açılan kullanıcıyı kayda bağlar.</summary>
    public void AttachIdentity(string subject)
    {
        if (EmailVerifiedAt is null)
        {
            throw new InvalidOperationException("E-postası doğrulanmamış kayıt için kullanıcı açılmaz.");
        }

        if (Subject is not null && Subject != subject)
        {
            throw new InvalidOperationException("Kayıt başka bir kullanıcıya bağlanmış.");
        }

        Subject = subject;
    }

    public void Complete(Guid accountId, DateTimeOffset now)
    {
        if (Subject is null)
        {
            throw new InvalidOperationException("Kullanıcısı açılmamış kayıt tamamlanmaz.");
        }

        AccountId = accountId;
        CompletedAt ??= now;
    }
}
