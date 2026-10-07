using HiWallet.Onboarding.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.Onboarding.Infrastructure.Persistence;

/// <summary>
/// Alan tipleri ↔ kolon değerleri. Okurken değer yeniden doğrulanıyor: veritabanına
/// elle yazılmış bozuk bir numara sessizce tipe girmesin.
/// </summary>
internal static class ValueConverters
{
    public static readonly ValueConverter<PhoneNumber, string> PhoneNumber =
        new(p => p.Value, text => ParsePhone(text));

    public static readonly ValueConverter<NationalId, string> NationalId =
        new(n => n.Value, text => Domain.NationalId.Parse(text));

    public static readonly ValueConverter<ConsentDocument, string> ConsentDocument =
        new(d => ToText(d), text => ToConsentDocument(text));

    private static PhoneNumber ParsePhone(string text) =>
        Domain.PhoneNumber.TryParse(text, out var phone)
            ? phone
            : throw new InvalidOperationException("Veritabanında geçersiz telefon numarası.");

    private static string ToText(ConsentDocument document) => document switch
    {
        Domain.ConsentDocument.Terms => "terms",
        Domain.ConsentDocument.PrivacyNotice => "privacy_notice",
        _ => throw new ArgumentOutOfRangeException(nameof(document), document, "Eşlemesi yazılmamış metin.")
    };

    private static ConsentDocument ToConsentDocument(string text) => text switch
    {
        "terms" => Domain.ConsentDocument.Terms,
        "privacy_notice" => Domain.ConsentDocument.PrivacyNotice,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen metin.")
    };
}
