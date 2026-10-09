namespace HiWallet.Onboarding.Domain;

/// <summary>
/// Numara değişikliğinin kaydı. Satır değişmiyor ve silinmiyor (veritabanında REVOKE):
/// eski numara müşteri bilgisinin geçmişi, hesabı kimin hangi numarayla kullandığının kanıtı.
/// </summary>
public sealed class PhoneChange
{
    private PhoneChange()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    /// <summary>Değişiklikten önceki numara; ilk numarası olmayan müşteride yok.</summary>
    public PhoneNumber? OldPhone { get; private set; }

    public PhoneNumber NewPhone { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public static PhoneChange Of(string subject, PhoneNumber? oldPhone, PhoneNumber newPhone, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Subject = subject,
        OldPhone = oldPhone,
        NewPhone = newPhone,
        ChangedAt = now
    };
}
