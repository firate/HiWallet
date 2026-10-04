namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Bireysel hesabın doğrulama seviyesi. Hangi para hareketinin ne kadar yapılabildiğini
/// seviye belirliyor (<see cref="Policies.KycLimitPolicy"/>); doğrulamanın kendisi ve
/// kişisel veri onboarding'de, burada yalnızca sonucu duruyor.
///
/// Değerler arasında boşluk var: araya bir seviye girebilsin.
/// </summary>
public enum KycLevel
{
    /// <summary>Kayıt bitti, kimlik doğrulanmadı. Geçici; para hareketi yok.</summary>
    Unknown = 10,

    /// <summary>
    /// Telefon doğrulandı, kimlik bilgileri nüfus kaydıyla eşleşti. Bilgiler doğrulandı,
    /// kişinin kendisi değil: kimliği tespit edilmemiş müşteri.
    /// </summary>
    Unverified = 20,

    /// <summary>
    /// Uzaktan kimlik tespiti: kimlik kartının çipi, canlılık testi ve yüzün çipteki
    /// fotoğrafla karşılaştırılması, insan müdahalesi olmadan.
    /// </summary>
    Verified = 30,

    /// <summary>Kimliği bir çalışan doğruladı (görüntülü görüşme ya da yüz yüze) ve sözleşme kuruldu.</summary>
    Contracted = 40
}

public static class KycLevels
{
    /// <summary>
    /// Kimliği yasal anlamda tespit edilmiş mi. Edilmemiş müşterinin aylık yüklemesi ve
    /// bakiyesi yasal tavanın altında kalıyor (MASAK Genel Tebliği Sıra No 5, 2.2.11).
    /// </summary>
    public static bool IsIdentified(this KycLevel level) => level >= KycLevel.Verified;
}
