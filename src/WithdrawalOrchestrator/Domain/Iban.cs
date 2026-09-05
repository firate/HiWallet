using System.Diagnostics.CodeAnalysis;

namespace HiWallet.WithdrawalOrchestrator.Domain;

/// <summary>
/// Doğrulanmış IBAN. Tip olarak var çünkü taşıdığı garanti bir string'de duramaz:
/// <b>bu kontrol "komisyon koşulsuz iade edilir" kuralının taşıyıcısı</b>
/// (overview.md madde 6).
///
/// Kuralın mantığı şu: başarısız bir çekimde komisyonu müşteriye koşulsuz iade
/// ediyoruz, çünkü başarısızlığın sebebi ya bizde ya bankada. Müşteri kaynaklı tek
/// gerçekçi senaryo yanlış IBAN — ve o, saga hiç başlamadan sınırda eleniyor
/// (baseline.md madde 6). Bu kontrol zayıflarsa kural yalan olur.
///
/// Doğrulama YAPISAL: mod-97 checksum'ı yazım hatasını yakalıyor. "Bu hesap var mı,
/// açık mı" sorusunu cevaplamıyor — onu ancak banka bilir.
/// </summary>
public readonly record struct Iban
{
    private const int MinLength = 15;
    private const int MaxLength = 34;

    private readonly string? _value;

    private Iban(string value) => _value = value;

    /// <summary>Normalize edilmiş hali: boşluksuz, büyük harf.</summary>
    public string Value => _value ?? throw new InvalidOperationException(
        "Iban initialize edilmemiş. default(Iban) kullanılamaz.");

    /// <summary>
    /// Log'a ve hata mesajlarına giden hali. Tam IBAN dolaştırmak gereksiz — son dört
    /// hane "hangi hesap" sorusunu cevaplamaya yetiyor.
    /// </summary>
    public string Masked
    {
        get
        {
            var value = Value;

            return string.Concat(value[..4], new string('*', value.Length - 8), value[^4..]);
        }
    }

    public override string ToString() => Masked;

    public static Iban From(string? value)
    {
        if (!TryFrom(value, out var iban))
        {
            // Mesajda ham girdi YOK: geçersiz de olsa müşteri verisi ve log'a düşüyor.
            throw new ArgumentException("Geçersiz IBAN.", nameof(value));
        }

        return iban;
    }

    /// <summary>
    /// Girdi tamamen dış dünyadan geliyor; hiçbir biçim bozukluğu istisnaya
    /// dönüşmemeli. Dönseydi sınırda <c>400</c> yerine <c>500</c> alırdık.
    /// </summary>
    public static bool TryFrom(string? value, out Iban iban)
    {
        iban = default;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = Normalize(value);

        if (normalized.Length is < MinLength or > MaxLength) return false;

        // İlk iki karakter ülke kodu (harf), sonraki iki karakter kontrol basamağı (rakam).
        if (!char.IsAsciiLetter(normalized[0]) || !char.IsAsciiLetter(normalized[1])) return false;
        if (!char.IsAsciiDigit(normalized[2]) || !char.IsAsciiDigit(normalized[3])) return false;

        // 00, 01 ve 99 standart gereği geçersiz. Mod-97 hesabı bunları kendi başına
        // elemiyor — ayrıca bakılması gerekiyor.
        var checkDigits = (normalized[2] - '0') * 10 + (normalized[3] - '0');
        if (checkDigits is 0 or 1 or 99) return false;

        if (!Mod97(normalized, out var remainder)) return false;
        if (remainder != 1) return false;

        iban = new Iban(normalized);
        return true;
    }

    private static string Normalize(string value)
    {
        // Banka arayüzleri IBAN'ı dörderli gruplayarak gösteriyor ve kullanıcı öyle
        // yapıştırıyor. Boşluk yüzünden reddetmek gereksiz sürtünme.
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character)) continue;

            buffer[length++] = char.ToUpperInvariant(character);
        }

        return new string(buffer[..length]);
    }

    /// <summary>
    /// ISO 13616 mod-97: ilk dört karakter sona alınır, harfler
    /// <c>A=10 … Z=35</c> ile sayıya çevrilir, kalan 1 olmalı.
    ///
    /// Sayı 34 haneye kadar çıkabildiği için <c>long</c>'a bile sığmıyor;
    /// <c>BigInteger</c> yerine basamak basamak kalan alınıyor — tahsisatsız ve
    /// aynı sonucu veriyor.
    /// </summary>
    private static bool Mod97(ReadOnlySpan<char> value, out int remainder)
    {
        remainder = 0;

        // Dört karakterlik ön ek sona kaydırılıyor: önce gövde, sonra ön ek.
        for (var pass = 0; pass < 2; pass++)
        {
            var segment = pass == 0 ? value[4..] : value[..4];

            foreach (var character in segment)
            {
                int digitPair;

                if (char.IsAsciiDigit(character))
                {
                    remainder = (remainder * 10 + (character - '0')) % 97;
                    continue;
                }

                if (!char.IsAsciiLetterUpper(character)) return false;

                // Harf iki basamaklı bir sayıya açılıyor (A=10 … Z=35).
                digitPair = character - 'A' + 10;
                remainder = (remainder * 100 + digitPair) % 97;
            }
        }

        return true;
    }
}
