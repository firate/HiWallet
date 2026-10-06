namespace HiWallet.Onboarding.Domain;

/// <summary>
/// Telefona SMS ile giden kod. Kayıttaki e-posta koduyla aynı kurallar: kod düz
/// saklanmıyor, on dakika geçerli, beş yanlış denemede kilitleniyor. Doğrulamayı yalnızca
/// başlatan kimlik tamamlayabiliyor.
/// </summary>
public sealed class PhoneVerification
{
    private PhoneVerification()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    /// <summary>Doğrulamayı başlatan kimlik.</summary>
    public string Subject { get; private set; } = string.Empty;

    public PhoneNumber Phone { get; private set; }

    public string CodeHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PhoneVerification Start(Guid id, string subject, PhoneNumber phone, string code, DateTimeOffset now) => new()
    {
        Id = id,
        Subject = subject,
        Phone = phone,
        CodeHash = VerificationCode.Hash(id, code),
        ExpiresAt = now + Registration.CodeLifetime,
        CreatedAt = now
    };

    public CodeCheck Confirm(string code, DateTimeOffset now)
    {
        if (VerifiedAt is not null) return CodeCheck.AlreadyVerified;
        if (FailedAttempts >= Registration.MaxAttempts) return CodeCheck.Locked;
        if (now > ExpiresAt) return CodeCheck.Expired;

        if (!VerificationCode.Matches(Id, code, CodeHash))
        {
            FailedAttempts++;
            return CodeCheck.WrongCode;
        }

        VerifiedAt = now;
        return CodeCheck.Verified;
    }
}
