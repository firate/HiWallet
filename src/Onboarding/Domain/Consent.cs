namespace HiWallet.Onboarding.Domain;

/// <summary>Onaylanan metin.</summary>
public enum ConsentDocument
{
    /// <summary>Kullanıcı sözleşmesi.</summary>
    Terms,

    /// <summary>KVKK aydınlatma metni.</summary>
    PrivacyNotice
}

/// <summary>
/// Müşterinin bir metnin belirli bir sürümünü onayladığı kayıt. Satır değişmiyor ve
/// silinmiyor (veritabanında REVOKE): onayın kanıtı. Yeni sürüm yeni satır.
/// </summary>
public sealed class Consent
{
    private Consent()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public ConsentDocument Document { get; private set; }

    public string Version { get; private set; } = string.Empty;

    public DateTimeOffset AcceptedAt { get; private set; }

    public static Consent Accept(string subject, ConsentDocument document, string version, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Subject = subject,
        Document = document,
        Version = version,
        AcceptedAt = now
    };
}
