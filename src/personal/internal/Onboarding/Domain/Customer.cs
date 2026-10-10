namespace HiWallet.Onboarding.Domain;

/// <summary>
/// Müşterinin kişisel verisi ve temel doğrulamanın adımları. Anahtar kimlik sağlayıcıdaki
/// <c>sub</c>; wallet'taki hesaba kayıt üzerinden bağlı. Ledger bu tabloyu görmüyor,
/// yalnızca doğrulamanın sonucunu (seviye) biliyor.
/// </summary>
public sealed class Customer
{
    private Customer()
    {
        // EF Core materialization.
    }

    public string Subject { get; private set; } = string.Empty;

    public PhoneNumber? Phone { get; private set; }

    public DateTimeOffset? PhoneVerifiedAt { get; private set; }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    /// <summary>Bir kimlik numarası tek müşteriye ait (unique index).</summary>
    public NationalId? NationalId { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    /// <summary>Kimlik bilgileri nüfus kaydıyla eşleşti.</summary>
    public DateTimeOffset? IdentityVerifiedAt { get; private set; }

    /// <summary>Telefon, kimlik ve onaylar tamamlandı; hesap temel seviyeye geçti.</summary>
    public DateTimeOffset? BasicVerifiedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Customer New(string subject, DateTimeOffset now) => new()
    {
        Subject = subject,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void PhoneVerified(PhoneNumber phone, DateTimeOffset now)
    {
        Phone = phone;
        PhoneVerifiedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Temel doğrulamadan sonra numara değişikliği; yeni numaranın kodu doğrulandı. Eski
    /// numara değişiklik kaydında kalıyor.
    /// </summary>
    /// <returns>Değişiklikten önceki numara.</returns>
    public PhoneNumber? ChangePhone(PhoneNumber phone, DateTimeOffset now)
    {
        if (BasicVerifiedAt is null)
        {
            throw new InvalidOperationException("Temel doğrulamayı bitirmemiş müşterinin numarası ilk doğrulamayla değişiyor.");
        }

        var old = Phone;
        Phone = phone;
        PhoneVerifiedAt = now;
        UpdatedAt = now;
        return old;
    }

    /// <summary>
    /// Nüfus kaydıyla eşleşen kimlik bilgileri. Temel doğrulama tamamlandıktan sonra
    /// değişmiyor: seviye bu bilgilere dayanarak verildi.
    /// </summary>
    public void IdentityVerified(string firstName, string lastName, NationalId nationalId, DateOnly birthDate, DateTimeOffset now)
    {
        if (BasicVerifiedAt is not null)
        {
            throw new InvalidOperationException("Temel doğrulaması tamamlanmış müşterinin kimlik bilgileri değişmez.");
        }

        FirstName = firstName;
        LastName = lastName;
        NationalId = nationalId;
        BirthDate = birthDate;
        IdentityVerifiedAt = now;
        UpdatedAt = now;
    }

    public bool ReadyForBasicVerification => PhoneVerifiedAt is not null && IdentityVerifiedAt is not null;

    public void BasicVerified(DateTimeOffset now)
    {
        if (!ReadyForBasicVerification)
        {
            throw new InvalidOperationException("Telefon ve kimlik doğrulanmadan temel doğrulama tamamlanmaz.");
        }

        BasicVerifiedAt ??= now;
        UpdatedAt = now;
    }
}
